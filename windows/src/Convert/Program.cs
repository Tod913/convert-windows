using Convert.Core;

namespace Convert;

/// <summary>
/// Where Convert starts. With several files selected, Explorer's classic menu starts one Convert per
/// file, and all but the first only pass their file on to the first one's window (SelectionGate). They
/// do that here, before the window system loads, so each of them ends in a fraction of the time.
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Convert.exe --open file… (Convert…, Send to) or --quick <format> file… (Convert to ▸ format).
        var (files, quick) = args switch
        {
            ["--open", _, ..] => (args[1..], null),
            ["--quick", _, _, ..] => (args[2..], args[1]),
            _ => ((string[]?)null, (string?)null),
        };
        if (files != null)
        {
            var paths = files.Select(a => Path.GetFullPath(a.Trim('"'))).ToList();
            // A quick conversion gathers only with others of the same format, never into an open window.
            var gate = SelectionGate.Open(paths, SelectionGate.DefaultName + (quick == null ? "" : ".quick." + quick));
            if (gate == null) return; // another Convert took them; nothing was drawn, so a normal exit is fine
            App.Gathered = new Gathering(paths, quick, gate);
        }

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}

/// <summary>Files from the classic menu that this process opens the window for, gathering the rest.</summary>
internal sealed record Gathering(List<string> Files, string? Quick, SelectionGate Gate);
