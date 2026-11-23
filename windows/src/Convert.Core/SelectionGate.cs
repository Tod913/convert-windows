using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;

namespace Convert.Core;

/// <summary>
/// Gathers a selection that Explorer hands over one file at a time into one window.
/// </summary>
/// <remarks>
/// Explorer's classic right-click menu ("Show more options") starts <c>Convert.exe --open file</c> once
/// per selected file. The first of those processes opens the window and listens on a named pipe; the
/// others send it their file and end. A pipe instance is always waiting while the window listens
/// (the next one is made before the current one is answered), so a process either finds the pipe or
/// becomes the listener itself: <see cref="PipeOptions.FirstPipeInstance"/> lets only one process
/// create it. The pipe only accepts the current user (<see cref="PipeOptions.CurrentUserOnly"/>), and
/// what arrives is only ever a list of files to show in the window, never something to convert.
/// The window stops listening after <see cref="IdleTime"/> without a new file, when it starts converting,
/// or when it closes; a later selection then opens a window of its own.
/// </remarks>
public sealed class SelectionGate : IDisposable
{
    public static readonly TimeSpan IdleTime = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ConnectTime = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan AnswerTime = TimeSpan.FromSeconds(10);
    private const int MaxFiles = 100_000;
    private static readonly UTF8Encoding Utf8 = new(false);

    private readonly string name;
    private NamedPipeServerStream? first;
    private readonly CancellationTokenSource stop = new();

    private SelectionGate(string name, NamedPipeServerStream? first)
    {
        this.name = name;
        this.first = first;
    }

    /// <summary>Ends when the gate has stopped listening (at once for a gate that never listened).</summary>
    public Task Completed { get; private set; } = Task.CompletedTask;

    /// <summary>The pipe's name: one per user and Windows session.</summary>
    public static string DefaultName
    {
        get
        {
            using var me = WindowsIdentity.GetCurrent();
            using var process = Process.GetCurrentProcess();
            return $"Convert.Open.{me.User?.Value ?? Environment.UserName}.{process.SessionId}";
        }
    }

    /// <summary>
    /// Returns a gate when this process should open the window for <paramref name="files"/>, or null when
    /// they were handed to the window of another Convert process and this one should end.
    /// </summary>
    public static SelectionGate? Open(IReadOnlyList<string> files, string? name = null)
    {
        name ??= DefaultName;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (TryCreate(name, first: true) is { } server) return new SelectionGate(name, server);
            switch (HandOver(name, files))
            {
                case Answer.Taken: return null;
                // That window is converting: open one of our own.
                case Answer.Refused: return new SelectionGate(name, null);
            }
            // The listener was just ending, or busy for too long: try again, maybe as the listener.
            Thread.Sleep(100);
        }
        return new SelectionGate(name, null);
    }

    /// <summary>
    /// Starts listening. <paramref name="accept"/> is called on a background thread with the files another
    /// process sent, and answers whether the window took them.
    /// </summary>
    public void Listen(Func<IReadOnlyList<string>, bool> accept)
    {
        if (Interlocked.Exchange(ref first, null) is not { } server) return;
        Completed = Task.Run(async () =>
        {
            var current = server;
            try
            {
                while (true)
                {
                    using var idle = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                    idle.CancelAfter(IdleTime);
                    try { await current.WaitForConnectionAsync(idle.Token); }
                    catch (OperationCanceledException) { return; }
                    catch (IOException) { return; }
                    // Before answering: the name must exist at all times while listening.
                    var next = TryCreate(name, first: false);
                    var taken = await ServeAsync(current, accept);
                    current.Dispose();
                    if (next == null) return;
                    current = next;
                    if (!taken) return; // the window is busy: later selections open their own
                }
            }
            finally { current.Dispose(); }
        });
    }

    /// <summary>Stops listening.</summary>
    public void Dispose()
    {
        stop.Cancel();
        Interlocked.Exchange(ref first, null)?.Dispose();
    }

    private enum Answer { Taken, Refused, Failed }

    private static NamedPipeServerStream? TryCreate(string name, bool first)
    {
        var options = PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly;
        if (first) options |= PipeOptions.FirstPipeInstance;
        try
        {
            return new NamedPipeServerStream(name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte, options);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Reads one process's files (one path per line, then an empty line) and answers "ok" or "no".</summary>
    private static async Task<bool> ServeAsync(NamedPipeServerStream pipe, Func<IReadOnlyList<string>, bool> accept)
    {
        using var timeout = new CancellationTokenSource(AnswerTime);
        try
        {
            using var reader = new StreamReader(pipe, Utf8, false, 4096, leaveOpen: true);
            var paths = new List<string>();
            while (paths.Count <= MaxFiles && await reader.ReadLineAsync(timeout.Token) is { Length: > 0 } line) paths.Add(line);
            var taken = paths.Count is > 0 and <= MaxFiles && paths.All(Path.IsPathFullyQualified) && accept(paths);
            await pipe.WriteAsync(Utf8.GetBytes(taken ? "ok\n" : "no\n"), timeout.Token);
            await pipe.FlushAsync(timeout.Token);
            return taken;
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException)
        {
            return true; // that process gave up and opens its own window; keep listening for the others
        }
    }

    private static Answer HandOver(string name, IReadOnlyList<string> files)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            pipe.Connect(ConnectTime);
            using var timeout = new CancellationTokenSource(AnswerTime);
            var text = string.Concat(files.Select(f => f + "\n")) + "\n";
            pipe.WriteAsync(Utf8.GetBytes(text), timeout.Token).AsTask().GetAwaiter().GetResult();
            pipe.Flush();
            using var reader = new StreamReader(pipe, Utf8, false, 64, leaveOpen: true);
            return reader.ReadLineAsync(timeout.Token).AsTask().GetAwaiter().GetResult() switch
            {
                "ok" => Answer.Taken,
                "no" => Answer.Refused,
                _ => Answer.Failed,
            };
        }
        catch (Exception e) when (e is IOException or TimeoutException or OperationCanceledException or UnauthorizedAccessException)
        {
            return Answer.Failed;
        }
    }
}
