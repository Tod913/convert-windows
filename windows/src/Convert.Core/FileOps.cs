using System.Runtime.InteropServices;

namespace Convert.Core;

public static class FileOps
{
    /// <summary>
    /// Whether an original may go to the Recycle Bin (which keeps it recoverable): only once its
    /// finished, separate output sits next to it, or in the folder the user chose. Never after Stop, and
    /// never when the output had to be saved somewhere else than asked.
    /// </summary>
    public static bool MayRecycle(string original, IReadOnlyCollection<string> outputs, bool stopped) =>
        !stopped && outputs.Count > 0
        && !outputs.Contains(original, StringComparer.OrdinalIgnoreCase)
        && outputs.All(o => File.Exists(o) || Directory.Exists(o))
        && !outputs.Any(o => OutputFolder.IsFallback(o, original));

    /// <summary>
    /// Moves a file to the Recycle Bin, where it can be restored from. Returns false, and leaves the
    /// file where it is, when that isn't possible.
    /// </summary>
    /// <remarks>
    /// Not every place has a Recycle Bin: network shares and many USB sticks don't, and a file can be
    /// too big for the bin. Windows' answer to "recycle" is then to delete for good, and with its
    /// questions switched off (there's nobody to ask in the middle of a batch) it does so silently.
    /// Three things stand in the way of that:
    /// a file bigger than the bin's limit for its drive isn't sent at all;
    /// Windows is asked what it is about to do, just before it does it, and told to leave the file
    /// alone unless the answer is "recycle" (that catches drives without a bin);
    /// and since "recycle" still means "or delete, if it doesn't fit", the file gets a second name
    /// for the duration (a hard link), under which it is put back if Windows deleted it after all.
    /// </remarks>
    /// <param name="checkBinSize">False only in tests, to see the last of those three work on its own.</param>
    public static bool Recycle(string path, bool checkBinSize = true)
    {
        var full = Path.GetFullPath(path);
        if (!File.Exists(full)) return false;
        var fits = checkBinSize ? FitsInRecycleBin(full) : null;
        if (fits == false) return false;
        var spare = SpareName(full);
        // Without a spare name there's no way back, so the bin's size limit has to be known for sure.
        if (spare == null && fits != true) return false;

        var recycled = false;
        // The shell's file operations need a single-threaded apartment; the caller may be on any thread.
        var thread = new Thread(() =>
        {
            try { recycled = RecycleOnThisThread(full); }
            catch (Exception e) when (e is COMException or InvalidCastException or ArgumentException or IOException or UnauthorizedAccessException) { recycled = false; }
        })
        { IsBackground = true, Name = "Convert recycle" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (spare != null)
        {
            try
            {
                // Gone, but not into the bin: put it back under its own name.
                if (!recycled && !File.Exists(full)) File.Move(spare, full);
                else File.Delete(spare);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return recycled && !File.Exists(full);
    }

    /// <summary>Gives the file a second name next to it, or returns null where the file system can't (FAT, exFAT).</summary>
    private static string? SpareName(string path)
    {
        var spare = $"{path}.{Guid.NewGuid():N}.convert-keep";
        return CreateHardLink(spare, path, IntPtr.Zero) ? spare : null;
    }

    /// <summary>
    /// Whether the file is within the Recycle Bin's size limit for its drive: true or false when
    /// Explorer has recorded a limit for that drive, null when it hasn't (or the path isn't on a drive).
    /// </summary>
    private static bool? FitsInRecycleBin(string path)
    {
        try
        {
            var root = new System.Text.StringBuilder(path.Length + 2);
            if (!GetVolumePathName(path, root, (uint)root.Capacity)) return null;
            var volume = new System.Text.StringBuilder(64);
            if (!GetVolumeNameForVolumeMountPoint(root.ToString(), volume, (uint)volume.Capacity)) return null;
            var name = volume.ToString();   // \\?\Volume{guid}\
            var open = name.IndexOf('{');
            var close = name.IndexOf('}');
            if (open < 0 || close < open) return null;
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\BitBucket\Volume\" + name[open..(close + 1)]);
            if (key?.GetValue("MaxCapacity") is not int megabytes) return null;
            // "Don't move files to the Recycle Bin. Remove files immediately when deleted."
            if (key.GetValue("NukeOnDelete") is int nuke && nuke != 0) return false;
            return new FileInfo(path).Length <= megabytes * 1024L * 1024L;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        {
            return null;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string newName, string existingName, IntPtr security);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumePathName(string path, System.Text.StringBuilder root, uint length);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeNameForVolumeMountPoint(string mountPoint, System.Text.StringBuilder volume, uint length);

    private static bool RecycleOnThisThread(string path)
    {
        var op = (IFileOperation)new FileOperation();
        try
        {
            op.SetOperationFlags(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI | FOFX_RECYCLEONDELETE | FOFX_EARLYFAILURE);
            var iid = typeof(IShellItem).GUID;
            SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var item);
            var sink = new RecycleOnly();
            try
            {
                op.DeleteItem(item, sink);
                op.PerformOperations();
            }
            finally { Marshal.ReleaseComObject(item); }
            op.GetAnyOperationsAborted(out var aborted);
            return sink.Recycled && !aborted;
        }
        finally { Marshal.ReleaseComObject(op); }
    }

    /// <summary>Lets a delete go ahead only as a move to the Recycle Bin.</summary>
    private sealed class RecycleOnly : IFileOperationProgressSink
    {
        public bool Recycled { get; private set; }

        public int PreDeleteItem(uint flags, IShellItem item) =>
            (flags & TSF_DELETE_RECYCLE_IF_POSSIBLE) != 0 ? S_OK : E_ABORT;

        public int PostDeleteItem(uint flags, IShellItem item, int result, IShellItem? inRecycleBin)
        {
            // The item it became in the Recycle Bin is the proof that it went there.
            Recycled = result >= 0 && inRecycleBin != null;
            return S_OK;
        }

        public int StartOperations() => S_OK;
        public int FinishOperations(int result) => S_OK;
        public int PreRenameItem(uint flags, IShellItem item, string? newName) => S_OK;
        public int PostRenameItem(uint flags, IShellItem item, string? newName, int result, IShellItem? created) => S_OK;
        public int PreMoveItem(uint flags, IShellItem item, IShellItem destination, string? newName) => S_OK;
        public int PostMoveItem(uint flags, IShellItem item, IShellItem destination, string? newName, int result, IShellItem? created) => S_OK;
        public int PreCopyItem(uint flags, IShellItem item, IShellItem destination, string? newName) => S_OK;
        public int PostCopyItem(uint flags, IShellItem item, IShellItem destination, string? newName, int result, IShellItem? created) => S_OK;
        public int PreNewItem(uint flags, IShellItem destination, string? newName) => S_OK;
        public int PostNewItem(uint flags, IShellItem destination, string? newName, string? template, uint attributes, int result, IShellItem? created) => S_OK;
        public int UpdateProgress(uint total, uint soFar) => S_OK;
        public int ResetTimer() => S_OK;
        public int PauseTimer() => S_OK;
        public int ResumeTimer() => S_OK;
    }

    private const int S_OK = 0, E_ABORT = unchecked((int)0x80004004);
    private const uint FOF_SILENT = 0x4, FOF_NOCONFIRMATION = 0x10, FOF_ALLOWUNDO = 0x40, FOF_NOERRORUI = 0x400;
    private const uint FOFX_RECYCLEONDELETE = 0x00080000, FOFX_EARLYFAILURE = 0x00100000;
    /// <summary>Set when the delete will be a move to the Recycle Bin; clear when the file would be gone for good.</summary>
    private const uint TSF_DELETE_RECYCLE_IF_POSSIBLE = 0x80;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid iid, out IShellItem item);

    // COM interfaces from shobjidl_core.h. Only the members used are spelled out; the rest keep their slot.

    [ComImport, Guid("3ad05575-8857-4850-9277-11b85bdb8e09")]
    private class FileOperation;

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem;

    [ComImport, Guid("947aab5f-0a5c-4c13-b4d6-4bf7836fc9f8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperation
    {
        void Advise(IFileOperationProgressSink sink, out uint cookie);
        void Unadvise(uint cookie);
        void SetOperationFlags(uint flags);
        void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
        void SetProgressDialog(IntPtr dialog);
        void SetProperties(IntPtr properties);
        void SetOwnerWindow(IntPtr window);
        void ApplyPropertiesToItem(IShellItem item);
        void ApplyPropertiesToItems(IntPtr items);
        void RenameItem(IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string newName, IFileOperationProgressSink? sink);
        void RenameItems(IntPtr items, [MarshalAs(UnmanagedType.LPWStr)] string newName);
        void MoveItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string? newName, IFileOperationProgressSink? sink);
        void MoveItems(IntPtr items, IShellItem destination);
        void CopyItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string? newName, IFileOperationProgressSink? sink);
        void CopyItems(IntPtr items, IShellItem destination);
        void DeleteItem(IShellItem item, IFileOperationProgressSink? sink);
        void DeleteItems(IntPtr items);
        void NewItem(IShellItem destination, uint attributes, [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string? template, IFileOperationProgressSink? sink);
        void PerformOperations();
        void GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
    }

    [ComImport, Guid("04b0f1a7-9490-44bc-96e1-4296a31252e2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperationProgressSink
    {
        [PreserveSig] int StartOperations();
        [PreserveSig] int FinishOperations(int result);
        [PreserveSig] int PreRenameItem(uint flags, IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string? newName);
        [PreserveSig] int PostRenameItem(uint flags, IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string? newName, int result, IShellItem? created);
        [PreserveSig] int PreMoveItem(uint flags, IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string? newName);
        [PreserveSig] int PostMoveItem(uint flags, IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string? newName, int result, IShellItem? created);
        [PreserveSig] int PreCopyItem(uint flags, IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string? newName);
        [PreserveSig] int PostCopyItem(uint flags, IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string? newName, int result, IShellItem? created);
        [PreserveSig] int PreDeleteItem(uint flags, IShellItem item);
        [PreserveSig] int PostDeleteItem(uint flags, IShellItem item, int result, IShellItem? inRecycleBin);
        [PreserveSig] int PreNewItem(uint flags, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string? newName);
        [PreserveSig] int PostNewItem(uint flags, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string? newName, [MarshalAs(UnmanagedType.LPWStr)] string? template, uint attributes, int result, IShellItem? created);
        [PreserveSig] int UpdateProgress(uint total, uint soFar);
        [PreserveSig] int ResetTimer();
        [PreserveSig] int PauseTimer();
        [PreserveSig] int ResumeTimer();
    }
}
