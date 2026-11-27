using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Win32;

namespace Convert.Core;

/// <summary>Thrown by a conversion the user stopped.</summary>
public sealed class StoppedException() : OperationCanceledException("Stopped");

/// <summary>
/// The external programs Convert uses when they're installed. They're looked up on every use, so
/// installing ffmpeg or LibreOffice while Convert is open takes effect without a restart.
/// </summary>
public static class Tools
{
    private static readonly string AppDir = AppContext.BaseDirectory;

    /// <summary>
    /// The folders a tool is looked for in, in this order: <c>tools\</c> next to Convert.exe (a bundled
    /// ffmpeg), the PATH Convert was started with, the PATH as it is now (installers add to it while
    /// Convert runs), then winget's link folder.
    /// </summary>
    public static IEnumerable<string> Directories()
    {
        yield return Path.Combine(AppDir, "tools");
        foreach (var d in Split(Environment.GetEnvironmentVariable("PATH"))) yield return d;
        foreach (var d in Split(RegistryPath(Registry.CurrentUser, "Environment"))) yield return d;
        foreach (var d in Split(RegistryPath(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment"))) yield return d;
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Links");
    }

    private static readonly string[] LibreOfficeDirectories =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LibreOffice", "program"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "LibreOffice", "program"),
    ];

    private static IEnumerable<string> Split(string? path) =>
        (path ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(d => d.Trim('"'));

    private static string? RegistryPath(RegistryKey hive, string key)
    {
        try
        {
            using var k = hive.OpenSubKey(key);
            return k?.GetValue("Path") as string;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return null; }
    }

    /// <summary>The first of <paramref name="directories"/> that holds <paramref name="exe"/>.</summary>
    public static string? Find(string exe, IEnumerable<string> directories)
    {
        foreach (var dir in directories)
        {
            // A relative entry would mean "whatever folder Convert was started in": the folder of the
            // files being converted, where anyone can have put an ffmpeg.exe.
            if (!Path.IsPathFullyQualified(dir)) continue;
            try
            {
                var p = Path.Combine(dir, exe);
                if (File.Exists(p)) return p;
            }
            catch (ArgumentException) { }
        }
        return null;
    }

    public static string? FFmpeg => Find("ffmpeg.exe", Directories());

    public static string? FFprobe
    {
        get
        {
            // The one that came with the ffmpeg in use, when there is one.
            var sibling = FFmpeg is { } ff ? Path.Combine(Path.GetDirectoryName(ff)!, "ffprobe.exe") : null;
            return sibling != null && File.Exists(sibling) ? sibling : Find("ffprobe.exe", Directories());
        }
    }

    public static string? LibreOffice => Find("soffice.exe", Directories().Concat(LibreOfficeDirectories));

    /// <summary>
    /// Identifies one installed copy of a tool: its path, size and date. What was found out about a
    /// tool (its encoders, whether the GPU works with it) is kept for as long as this stays the same.
    /// </summary>
    public static string Stamp(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return $"{path}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        }
        catch (IOException) { return path; }
        catch (UnauthorizedAccessException) { return path; }
    }

    private static readonly object encoderGate = new();
    private static string? encodersOf;
    private static HashSet<string> encoders = [];

    /// <summary>Whether the installed ffmpeg has this encoder. Asked again when ffmpeg changes.</summary>
    public static bool HasEncoder(string name)
    {
        if (FFmpeg is not { } ff) return false;
        var stamp = Stamp(ff);
        lock (encoderGate)
        {
            if (encodersOf != stamp)
            {
                encoders = EncoderCache.Read(stamp) ?? EncoderCache.Write(stamp, ListEncoders(ff));
                encodersOf = stamp;
            }
            return encoders.Contains(name);
        }
    }

    private static HashSet<string> ListEncoders(string ffmpeg)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            foreach (var line in Proc.RunSync(ffmpeg, ["-hide_banner", "-encoders"]).Split('\n'))
            {
                var parts = line.Trim().Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && parts[0].Length == 6) set.Add(parts[1]);
            }
        }
        catch (ConvertException) { }
        return set;
    }
}

/// <summary>
/// The encoders of one ffmpeg, kept on disk (%LOCALAPPDATA%\Convert\encoders.txt) under its <see cref="Tools.Stamp"/>.
/// What a copy of ffmpeg can encode never changes, and asking means starting it (a program of about 100 MB),
/// which would otherwise happen each time a Convert window opens.
/// </summary>
internal static class EncoderCache
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Convert", "encoders.txt");

    public static HashSet<string>? Read(string stamp)
    {
        try
        {
            var lines = File.ReadAllLines(FilePath);
            return lines.Length > 1 && lines[0] == stamp ? new HashSet<string>(lines.Skip(1), StringComparer.Ordinal) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Keeps <paramref name="encoders"/> for next time (not an empty list: ffmpeg may just have failed to start).</summary>
    public static HashSet<string> Write(string stamp, HashSet<string> encoders)
    {
        if (encoders.Count == 0) return encoders;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var tmp = $"{FilePath}.{Environment.ProcessId}.tmp";
            File.WriteAllLines(tmp, encoders.Order(StringComparer.Ordinal).Prepend(stamp));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return encoders;
    }
}

public static class Proc
{
    /// <summary>
    /// How much of a tool's error output is kept. Only the end matters (the last line is the message
    /// shown), so a tool that logs without end can't use up memory.
    /// </summary>
    public const int KeptErrorOutput = 64 * 1024;

    /// <summary>
    /// Runs a tool to completion and returns stdout. Throws with the last stderr line on failure.
    /// With a <paramref name="runner"/>, Stop ends the tool.
    /// </summary>
    public static string RunSync(string exe, IEnumerable<string> args, int timeoutMs = 120_000, Runner? runner = null)
    {
        runner?.CheckCancelled();
        var name = Path.GetFileNameWithoutExtension(exe);
        using var p = Start(exe, args);
        using var tracked = runner?.Track(p);
        // Both pipes are read at the same time: a tool that fills one while we wait on the other
        // would block for good.
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = ReadTailAsync(p.StandardError);
        if (!p.WaitForExit(timeoutMs))
        {
            Kill(p);
            throw new ConvertException($"{name} took too long");
        }
        p.WaitForExit();
        runner?.CheckCancelled();
        if (p.ExitCode != 0) throw new ConvertException(LastLine(stderr.Result) ?? $"{name} failed");
        return stdout.Result;
    }

    public static Process Start(string exe, IEnumerable<string> args)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        Process p;
        try { p = Process.Start(psi) ?? throw new ConvertException($"Couldn't start {Path.GetFileName(exe)}"); }
        catch (Win32Exception) { throw new ConvertException($"Couldn't start {Path.GetFileName(exe)}"); }
        p.StandardInput.Close();
        return p;
    }

    /// <summary>Ends a tool and everything it started (soffice.exe, for one, only starts soffice.bin).</summary>
    public static void Kill(Process? p)
    {
        try { p?.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }   // already gone
        catch (Win32Exception) { }
        catch (NotSupportedException) { }
    }

    /// <summary>Reads a tool's output to its end, keeping only the last <see cref="KeptErrorOutput"/> characters.</summary>
    public static async Task<string> ReadTailAsync(TextReader reader, int kept = KeptErrorOutput)
    {
        var text = new StringBuilder();
        var buffer = new char[8192];
        int n;
        while ((n = await reader.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            text.Append(buffer, 0, n);
            if (text.Length > 2 * kept) text.Remove(0, text.Length - kept);
        }
        if (text.Length > kept) text.Remove(0, text.Length - kept);
        return text.ToString();
    }

    public static string? LastLine(string text) =>
        text.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0);
}

/// <summary>
/// Turns ffmpeg's <c>-progress</c> output into fractions done (0…1). The output arrives in chunks of
/// any size, so a line can be cut in two; the unfinished end is kept until the rest of it arrives.
/// </summary>
public sealed class ProgressParser(double duration)
{
    private readonly StringBuilder pending = new();

    public List<double> Feed(ReadOnlySpan<char> chunk)
    {
        pending.Append(chunk);
        var fractions = new List<double>();
        while (true)
        {
            var text = pending.ToString();
            var newline = text.IndexOf('\n');
            if (newline < 0) break;
            var line = text[..newline];
            pending.Remove(0, newline + 1);
            const string key = "out_time_us=";
            if (duration > 0 && line.StartsWith(key, StringComparison.Ordinal)
                && double.TryParse(line.AsSpan(key.Length).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var us) && us >= 0)
                fractions.Add(Math.Min(1, us / 1_000_000 / duration));
        }
        return fractions;
    }
}

/// <summary>
/// One batch of conversions: runs ffmpeg-style processes with live progress, and carries Stop.
/// Stop kills the process that's running; work done in-process calls <see cref="CheckCancelled"/>
/// between steps, or watches <see cref="Token"/>.
/// </summary>
public sealed class Runner
{
    private readonly object gate = new();
    private Process? current;
    private bool cancelled;
    private CancellationTokenSource source = new();

    public bool IsCancelled
    {
        get { lock (gate) return cancelled; }
    }

    /// <summary>Cancelled by Stop, for work that waits on something (a page being printed, a file being written).</summary>
    public CancellationToken Token
    {
        get { lock (gate) return source.Token; }
    }

    public void Cancel()
    {
        CancellationTokenSource stopped;
        lock (gate)
        {
            cancelled = true;
            Proc.Kill(current);
            stopped = source;
        }
        stopped.Cancel();
    }

    /// <summary>Clears an earlier Stop, before a new batch.</summary>
    public void Reset()
    {
        lock (gate)
        {
            if (!cancelled) return;
            cancelled = false;
            source = new CancellationTokenSource();
        }
    }

    public void CheckCancelled()
    {
        if (IsCancelled) throw new StoppedException();
    }

    /// <summary>Makes <paramref name="p"/> the process Stop ends, until the returned object is disposed.</summary>
    public IDisposable Track(Process p)
    {
        lock (gate)
        {
            current = p;
            if (cancelled) Proc.Kill(p);
        }
        return new Tracked(this, p);
    }

    private sealed class Tracked(Runner runner, Process p) : IDisposable
    {
        public void Dispose()
        {
            lock (runner.gate)
            {
                if (ReferenceEquals(runner.current, p)) runner.current = null;
            }
        }
    }

    public async Task RunAsync(string exe, IEnumerable<string> args, double duration, Action<double> progress)
    {
        // A tool isn't even started once Stop was pressed.
        CheckCancelled();
        var name = Path.GetFileNameWithoutExtension(exe);
        using var p = Proc.Start(exe, args);
        using var tracked = Track(p);
        var err = Proc.ReadTailAsync(p.StandardError);
        var parser = new ProgressParser(duration);
        var buffer = new char[4096];
        int n;
        while ((n = await p.StandardOutput.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            foreach (var fraction in parser.Feed(buffer.AsSpan(0, n))) progress(fraction);
        }
        await p.WaitForExitAsync().ConfigureAwait(false);
        if (IsCancelled) throw new StoppedException();
        if (p.ExitCode == 0) return;
        // An exit code in this range is an exception code: the tool crashed, and whatever it logged
        // before that isn't the reason.
        if (unchecked((uint)p.ExitCode) >= 0xC0000000) throw new ConvertException($"{name} quit unexpectedly");
        throw new ConvertException(Proc.LastLine(await err.ConfigureAwait(false)) ?? $"{name} exited with code {p.ExitCode}");
    }
}
