using System.Runtime.InteropServices;

namespace Convert;

/// <summary>
/// Puts a converted file's desktop icon right below its original's, when both are on the desktop.
/// </summary>
/// <remarks>
/// Windows puts a new desktop icon in the first free place it finds, often far from the original. The
/// desktop is a folder view in Explorer, and <see cref="IFolderView"/> can move its icons
/// (<c>SelectAndPositionItems</c>). Nothing happens when the desktop arranges its icons by itself
/// ("Auto arrange icons"), or when anything here doesn't work out: it is a nicety, never an error.
/// </remarks>
internal static class DesktopIcons
{
    /// <summary>How long to wait for a new file to show up on the desktop.</summary>
    private static readonly TimeSpan Appear = TimeSpan.FromSeconds(3);

    public static async Task PlaceAsync(IReadOnlyList<(string Original, string Output)> made)
    {
        var onDesktop = made.Where(m => OnDesktop(m.Original) && OnDesktop(m.Output)).ToList();
        if (onDesktop.Count == 0) return;
        try
        {
            if (DesktopView() is not { } view || view.GetAutoArrange() == 0 /* S_OK: arranged by Windows */) return;
            if (view.GetSpacing(out var spacing) != 0 || spacing.X <= 0 || spacing.Y <= 0) return;
            var waitUntil = DateTime.UtcNow + Appear;
            foreach (var (original, output) in onDesktop)
            {
                using var from = Pidl.Parse(original);
                using var item = Pidl.Parse(output);
                if (from == null || item == null || view.GetItemPosition(from.Child, out var at) != 0) continue;
                Point now;
                while (view.GetItemPosition(item.Child, out now) != 0)
                {
                    if (DateTime.UtcNow > waitUntil) return;
                    await Task.Delay(100);
                }
                if (FreeSpot(view, at, now, spacing) is { } spot)
                    view.SelectAndPositionItems(1, [item.Child], [spot], SVSI_POSITIONITEM);
            }
        }
        catch (Exception e) when (e is COMException or InvalidCastException or ArgumentException) { }
    }

    private static bool OnDesktop(string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        return Same(dir, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory))
               || Same(dir, Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory));
    }

    private static bool Same(string? a, string b) =>
        a != null && b.Length > 0
        && string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The first free place below the original (<paramref name="at"/>), then in the columns to its right,
    /// within the screen. <paramref name="self"/> is where the new icon is now, which doesn't count as taken.
    /// </summary>
    private static Point? FreeSpot(IFolderView view, Point at, Point self, Point spacing)
    {
        var taken = Positions(view).Where(p => p.X != self.X || p.Y != self.Y).ToList();
        var width = GetSystemMetrics(78 /* SM_CXVIRTUALSCREEN */);
        var height = GetSystemMetrics(79 /* SM_CYVIRTUALSCREEN */) - spacing.Y / 2; // the taskbar
        for (var column = 0; column < 6; column++)
        {
            for (var row = column == 0 ? 1 : 0; row < 30; row++)
            {
                var spot = new Point { X = at.X + column * spacing.X, Y = at.Y + row * spacing.Y };
                if (spot.X + spacing.X > width || spot.Y + spacing.Y > height) break;
                if (!taken.Any(p => Math.Abs(p.X - spot.X) < spacing.X / 2 && Math.Abs(p.Y - spot.Y) < spacing.Y / 2))
                    return spot;
            }
        }
        return null;
    }

    private static List<Point> Positions(IFolderView view)
    {
        var list = new List<Point>();
        var iid = typeof(IEnumIDList).GUID;
        if (view.Items(SVGIO_ALLVIEW, ref iid, out var found) != 0 || found is not IEnumIDList items) return list;
        while (items.Next(1, out var child, out var fetched) == 0 && fetched == 1)
        {
            try
            {
                if (view.GetItemPosition(child, out var p) == 0) list.Add(p);
            }
            finally { Marshal.FreeCoTaskMem(child); }
        }
        return list;
    }

    /// <summary>The desktop's folder view, from Explorer (Raymond Chen's recipe).</summary>
    private static IFolderView? DesktopView()
    {
        var type = Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39")); // ShellWindows
        if (type == null || Activator.CreateInstance(type) is not IShellWindows windows) return null;
        object location = 0; // CSIDL_DESKTOP
        object root = null!;
        if (windows.FindWindowSW(ref location, ref root, SWC_DESKTOP, out _, SWFO_NEEDDISPATCH, out var found) != 0
            || found is not IServiceProvider provider) return null;
        var service = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837"); // SID_STopLevelBrowser
        var iid = typeof(IShellBrowser).GUID;
        if (provider.QueryService(ref service, ref iid, out var b) != 0 || b is not IShellBrowser browser) return null;
        return browser.QueryActiveShellView(out var view) == 0 ? view as IFolderView : null;
    }

    private const int SWC_DESKTOP = 8, SWFO_NEEDDISPATCH = 1;
    private const uint SVGIO_ALLVIEW = 2, SVSI_POSITIONITEM = 0x80;

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X, Y;
    }

    /// <summary>An item ID list for a path, and its last part (the item in its folder).</summary>
    private sealed class Pidl : IDisposable
    {
        private IntPtr full;
        public IntPtr Child { get; }

        private Pidl(IntPtr full)
        {
            this.full = full;
            Child = ILFindLastID(full);
        }

        public static Pidl? Parse(string path) =>
            SHParseDisplayName(path, IntPtr.Zero, out var pidl, 0, out _) == 0 && pidl != IntPtr.Zero ? new Pidl(pidl) : null;

        public void Dispose()
        {
            Marshal.FreeCoTaskMem(full);
            full = IntPtr.Zero;
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, IntPtr bindContext, out IntPtr pidl, uint sfgaoIn, out uint sfgaoOut);

    [DllImport("shell32.dll")]
    private static extern IntPtr ILFindLastID(IntPtr pidl);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    // Only the methods called here have real signatures; the others hold their place in the vtable.

    [ComImport, Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IShellWindows
    {
        [PreserveSig] int get_Count();
        [PreserveSig] int Item();
        [PreserveSig] int _NewEnum();
        [PreserveSig] int Register();
        [PreserveSig] int RegisterPending();
        [PreserveSig] int Revoke();
        [PreserveSig] int OnNavigate();
        [PreserveSig] int OnActivated();
        [PreserveSig]
        int FindWindowSW([In, MarshalAs(UnmanagedType.Struct)] ref object location, [In, MarshalAs(UnmanagedType.Struct)] ref object root,
            int swClass, out int hwnd, int options, [MarshalAs(UnmanagedType.IDispatch)] out object dispatch);
    }

    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider
    {
        [PreserveSig] int QueryService(ref Guid service, ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object result);
    }

    [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        [PreserveSig] int GetWindow();
        [PreserveSig] int ContextSensitiveHelp();
        [PreserveSig] int InsertMenusSB();
        [PreserveSig] int SetMenuSB();
        [PreserveSig] int RemoveMenusSB();
        [PreserveSig] int SetStatusTextSB();
        [PreserveSig] int EnableModelessSB();
        [PreserveSig] int TranslateAcceleratorSB();
        [PreserveSig] int BrowseObject();
        [PreserveSig] int GetViewStateStream();
        [PreserveSig] int GetControlWindow();
        [PreserveSig] int SendControlMsg();
        [PreserveSig] int QueryActiveShellView([MarshalAs(UnmanagedType.IUnknown)] out object view);
    }

    [ComImport, Guid("CDE725B0-CCC9-4519-917E-325D72FAB4CE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFolderView
    {
        [PreserveSig] int GetCurrentViewMode();
        [PreserveSig] int SetCurrentViewMode();
        [PreserveSig] int GetFolder();
        [PreserveSig] int Item();
        [PreserveSig] int ItemCount();
        [PreserveSig] int Items(uint flags, ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object items);
        [PreserveSig] int GetSelectionMarkedItem();
        [PreserveSig] int GetFocusedItem();
        [PreserveSig] int GetItemPosition(IntPtr child, out Point position);
        [PreserveSig] int GetSpacing(out Point spacing);
        [PreserveSig] int GetDefaultSpacing();
        [PreserveSig] int GetAutoArrange();
        [PreserveSig] int SelectItem();
        [PreserveSig]
        int SelectAndPositionItems(uint count, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] children,
            [MarshalAs(UnmanagedType.LPArray)] Point[] positions, uint flags);
    }

    [ComImport, Guid("000214F2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumIDList
    {
        [PreserveSig] int Next(uint count, out IntPtr child, out uint fetched);
    }
}
