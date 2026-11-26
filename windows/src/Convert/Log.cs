namespace Convert;

/// <summary>
/// A few lines about what the window decided, in %LOCALAPPDATA%\Convert\convert.log, to find out why
/// something didn't happen on a PC we can't look at. No file names; it stays on the PC, and is kept small.
/// </summary>
internal static class Log
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Convert", "convert.log");

    public static void Write(string line)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 64 * 1024) File.Delete(FilePath);
            File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Environment.ProcessId}] {line}{Environment.NewLine}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
