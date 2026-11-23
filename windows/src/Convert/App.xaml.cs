using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using Convert.Core;

namespace Convert;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = e.Args;

        // Command-line modes (no window).
        if (args.Length > 0 && args[0] == "--list-extensions")
        {
            Cli.Attach();
            foreach (var ext in MediaKinds.AllExtensions) Console.WriteLine(ext);
            AppExit.Now(0);
            return;
        }
        // For the installer: put "Convert…" in Explorer's right-click menu, or take it out.
        if (args.Length > 0 && args[0] is "--register-menu" or "--unregister-menu" or "--menu-status")
        {
            Cli.Attach();
            var work = args[0] switch
            {
                "--register-menu" => ShellMenu.RegisterAsync(),
                "--unregister-menu" => ShellMenu.UnregisterAsync(),
                _ => Task.Run(ShellMenu.Status),
            };
            work.ContinueWith(t => AppExit.Now(t.IsFaulted ? 1 : t.Result));
            return;
        }
        if (args.Length > 0 && args[0] == "--cli")
        {
            Cli.Attach();
            Cli.RunAsync(args[1..]).ContinueWith(t => AppExit.Now(t.IsFaulted ? 1 : t.Result));
            return;
        }

        // From the classic right-click menu and Send to: Explorer may start one Convert per selected
        // file, so the files are gathered into one window (SelectionGate).
        if (args.Length > 1 && args[0] == "--open")
        {
            OpenGathered(args[1..]);
            return;
        }

        var files = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            // The context menu passes long selections as a list file (one path per line) to stay under
            // the command-line length limit.
            if (args[i] == "--files" && i + 1 < args.Length) files.AddRange(SelectionList.Read(args[++i]));
            else files.Add(args[i]);
        }

        if (files.Any(f => MediaKinds.Of(f) is MediaKind.Video)) HardwareEncoder.Warmup();
        new MainWindow(new MainViewModel(files)).Show();
    }

    private void OpenGathered(string[] args)
    {
        var files = args.Select(a => Path.GetFullPath(a.Trim('"'))).ToList();
        var gate = SelectionGate.Open(files);
        if (gate == null)
        {
            AppExit.Now(0); // another Convert window took them
            return;
        }

        // For the setup program's test: write what was gathered to a file instead of opening a window.
        if (Environment.GetEnvironmentVariable("CONVERT_OPEN_REPORT") is { Length: > 0 } report)
        {
            var gathered = new List<string>(files);
            gate.Listen(more => { lock (gathered) gathered.AddRange(more); return true; });
            gate.Completed.ContinueWith(_ =>
            {
                lock (gathered) File.WriteAllLines(report, gathered);
                AppExit.Now(0);
            });
            return;
        }

        if (files.Any(f => MediaKinds.Of(f) is MediaKind.Video)) HardwareEncoder.Warmup();
        var vm = new MainViewModel(files);
        var window = new MainWindow(vm);
        window.Closed += (_, _) => gate.Dispose();
        gate.Listen(more =>
        {
            try
            {
                return Dispatcher.Invoke(() =>
                {
                    if (vm.Phase != Phase.Idle || !window.IsVisible) return false;
                    vm.Add(more);
                    return true;
                });
            }
            catch (OperationCanceledException) { return false; } // the app is closing
        });
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        base.OnExit(e);
        // The last window is closed and no conversion is running (the window waits for that): done.
        AppExit.Now(e.ApplicationExitCode);
    }
}

/// <summary>
/// Test mode: <c>Convert.exe --cli &lt;target-id&gt; file…</c> converts with default settings and prints the results.
/// <c>CONVERT_SETTINGS</c> (JSON) overrides individual settings, e.g. <c>{"hardware": false}</c>.
/// <c>Convert.exe --cli --targets file…</c> lists the target IDs the window would offer for those files.
/// The output and exit codes (0 fine, 1 a conversion failed, 2 wrong usage) are the Mac app's.
/// </summary>
internal static class Cli
{
    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);

    /// <summary>Convert.exe is a GUI app; borrow the parent console so Console output shows up.</summary>
    public static void Attach()
    {
        AttachConsole(-1);
        // "→" and file names outside the console's code page.
        try { Console.OutputEncoding = new UTF8Encoding(false); }
        catch (IOException) { }
    }

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--targets") return ListTargets(args[1..]);

        Target? target = args.Length >= 2 ? Targets.ById(args[0]) : null;
        if (target == null)
        {
            Console.WriteLine("usage: Convert.exe --cli <target> file…");
            Console.WriteLine("       Convert.exe --cli --targets file…");
            Console.WriteLine("targets: " + string.Join(" ", Targets.All.Select(t => t.Id)));
            return 2;
        }
        var settings = new ConvSettings();
        if (Environment.GetEnvironmentVariable("CONVERT_SETTINGS") is { Length: > 0 } json)
        {
            try { settings = ConvSettings.WithOverrides(json); }
            catch (ConvertException e)
            {
                Console.WriteLine($"CONVERT_SETTINGS is not valid: {e.Message}");
                return 2;
            }
        }

        var failed = false;
        foreach (var arg in args[1..])
        {
            var path = Path.GetFullPath(arg);
            var name = Path.GetFileName(path);
            var started = DateTime.UtcNow;
            try
            {
                var outs = await Converter.ConvertAsync(path, target, settings, new Runner(), _ => { });
                var secs = (DateTime.UtcNow - started).TotalSeconds.ToString("0.00", CultureInfo.InvariantCulture);
                Console.WriteLine($"OK   {name} → {string.Join(", ", outs.Select(Path.GetFileName))} ({secs}s)");
                if (outs.FirstOrDefault(o => OutputFolder.IsFallback(o, path)) is { } moved)
                    Console.WriteLine($"     saved to {Path.GetDirectoryName(moved)}: the original's folder is read-only");
            }
            catch (Exception ex)
            {
                failed = true;
                Console.WriteLine($"FAIL {name}: {ex.Message}");
            }
        }
        return failed ? 1 : 0;
    }

    /// <summary>Prints one target ID per line, the same choices the window offers for these files.</summary>
    private static int ListTargets(string[] paths)
    {
        var kinds = new HashSet<MediaKind>();
        foreach (var path in paths)
        {
            if (Sniff.KindOf(Path.GetFullPath(path)) is not { } kind)
            {
                Console.WriteLine($"unsupported file: {Path.GetFileName(path)}");
                return 1;
            }
            kinds.Add(kind);
        }
        foreach (var (_, targets) in Targets.Groups(kinds))
        {
            foreach (var target in targets) Console.WriteLine(target.Id);
        }
        return 0;
    }
}
