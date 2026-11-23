using System.Runtime.InteropServices;
using Convert.Core;
using Microsoft.Win32;

namespace Convert;

/// <summary>
/// Explorer's classic right-click menu ("Show more options" on Windows 11, or Shift + right-click), for a
/// Convert whose package Windows won't register because it isn't signed by a trusted publisher:
/// <c>Convert.exe --register-classic-menu</c> and <c>--unregister-classic-menu</c>, for the installer.
/// </summary>
/// <remarks>
/// For this user only, one set of entries per file type Convert takes:
/// "Convert…" opens the window, and "Convert to ▸" lists a few formats (<see cref="QuickTargets"/>)
/// that convert at once. Explorer starts Convert once per selected file ("Player" lifts its limit of
/// 15 files); <see cref="SelectionGate"/> gathers them.
/// </remarks>
internal static class ClassicMenu
{
    private const string Root = @"Software\Classes\SystemFileAssociations";
    private const string OpenVerb = "Convert", QuickVerb = "ConvertTo";

    private static string Exe => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Convert.exe");

    public static int Register()
    {
        try
        {
            Remove();
            var exe = Exe;
            var available = Targets.All;
            var extensions = MediaKinds.AllExtensions.ToList();
            foreach (var ext in extensions)
            {
                using (var open = Registry.CurrentUser.CreateSubKey($@"{Root}\.{ext}\shell\{OpenVerb}"))
                {
                    SetVerb(open, "Convert…", exe);
                    using var command = open.CreateSubKey("command");
                    command.SetValue("", $"\"{exe}\" --open \"%1\"");
                }
                var quick = QuickTargets.For(ext, available);
                if (quick.Count == 0) continue;
                using var menu = Registry.CurrentUser.CreateSubKey($@"{Root}\.{ext}\shell\{QuickVerb}");
                SetVerb(menu, "Convert to", exe);
                menu.SetValue("SubCommands", ""); // the entries are the keys under shell\, in name order
                for (var i = 0; i < quick.Count; i++)
                {
                    using var item = menu.CreateSubKey($@"shell\{i + 1:00}-{quick[i].Id}");
                    item.SetValue("MUIVerb", quick[i].Label);
                    item.SetValue("MultiSelectModel", "Player");
                    using var command = item.CreateSubKey("command");
                    command.SetValue("", $"\"{exe}\" --quick {quick[i].Id} \"%1\"");
                }
            }
            Notify();
            Console.WriteLine($"Added Convert… and Convert to to the classic right-click menu for {extensions.Count} file types.");
            return 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Console.WriteLine("Couldn't add Convert to the classic right-click menu: " + e.Message);
            return 1;
        }
    }

    public static int Unregister()
    {
        try
        {
            var removed = Remove();
            Notify();
            Console.WriteLine($"Removed Convert from the classic right-click menu ({removed} file types).");
            return 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Console.WriteLine("Couldn't remove Convert from the classic right-click menu: " + e.Message);
            return 1;
        }
    }

    private static void SetVerb(RegistryKey key, string label, string exe)
    {
        key.SetValue("MUIVerb", label);
        key.SetValue("Icon", $"\"{exe}\",0");
        key.SetValue("MultiSelectModel", "Player");
    }

    /// <summary>
    /// Removes the entries that start this Convert, including ones for file types an earlier version
    /// took. Other programs' entries, and a Convert installed somewhere else, are left alone.
    /// </summary>
    private static int Remove()
    {
        using var root = Registry.CurrentUser.OpenSubKey(Root, writable: true);
        if (root == null) return 0;
        var exe = Exe;
        var removed = 0;
        foreach (var name in root.GetSubKeyNames())
        {
            var shell = $@"{name}\shell";
            var found = false;
            foreach (var verb in new[] { OpenVerb, QuickVerb })
            {
                bool ours;
                using (var key = root.OpenSubKey($@"{shell}\{verb}")) ours = key != null && StartsThisConvert(key, exe);
                if (!ours) continue;
                root.DeleteSubKeyTree($@"{shell}\{verb}", throwOnMissingSubKey: false);
                found = true;
            }
            if (!found) continue;
            removed++;
            DeleteIfEmpty(root, shell);
            DeleteIfEmpty(root, name);
        }
        return removed;
    }

    /// <summary>Whether the verb's command, or a command under it, runs <paramref name="exe"/>.</summary>
    private static bool StartsThisConvert(RegistryKey verb, string exe)
    {
        using (var command = verb.OpenSubKey("command"))
            if (command?.GetValue("") is string line && line.Contains(exe, StringComparison.OrdinalIgnoreCase)) return true;
        using var items = verb.OpenSubKey("shell");
        if (items == null) return false;
        foreach (var item in items.GetSubKeyNames())
        {
            using var key = items.OpenSubKey(item);
            if (key != null && StartsThisConvert(key, exe)) return true;
        }
        return false;
    }

    private static void DeleteIfEmpty(RegistryKey root, string path)
    {
        using (var key = root.OpenSubKey(path))
            if (key == null || key.SubKeyCount > 0 || key.ValueCount > 0) return;
        root.DeleteSubKey(path, throwOnMissingSubKey: false);
    }

    /// <summary>Tells Explorer that file type entries changed, so the menu shows the change at once.</summary>
    private static void Notify() => SHChangeNotify(0x08000000 /* SHCNE_ASSOCCHANGED */, 0, IntPtr.Zero, IntPtr.Zero);

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
