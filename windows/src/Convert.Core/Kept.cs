using System.Globalization;

namespace Convert.Core;

/// <summary>
/// Answers that are slow to find out and rarely change, kept in small files in %LOCALAPPDATA%\Convert so
/// that the next Convert window doesn't find them out again: an ffmpeg's encoders (encoders.txt), whether
/// the GPU encodes (gpu.txt) and which image formats ImageMagick writes (formats.txt).
/// </summary>
/// <remarks>
/// Each file is kept under the identity of what it's about (<see cref="Tools.Stamp"/>: path, size and
/// date), and a different identity means the answer is found out again. The file holds that identity, the
/// time it was written and the answer's lines. A file that can't be read or written only costs the time
/// it was meant to save.
/// </remarks>
public static class Kept
{
    public static string Folder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Convert");

    /// <summary>
    /// The lines kept as <paramref name="name"/> for <paramref name="stamp"/>, or null when there are none,
    /// they're about something else, or they're older than <paramref name="maxAge"/>.
    /// </summary>
    public static string[]? Read(string name, string stamp, TimeSpan? maxAge = null)
    {
        try
        {
            var lines = File.ReadAllLines(Path.Combine(Folder, name));
            if (lines.Length < 2 || lines[0] != stamp || !long.TryParse(lines[1], CultureInfo.InvariantCulture, out var ticks)) return null;
            var age = DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc);
            if (maxAge is { } max && (age < TimeSpan.Zero || age >= max)) return null;
            return lines[2..];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }

    /// <summary>Keeps <paramref name="lines"/> as <paramref name="name"/> for <paramref name="stamp"/>, replacing what was there.</summary>
    public static void Write(string name, string stamp, IEnumerable<string> lines)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var path = Path.Combine(Folder, name);
            // Written next to it and moved into place, so another Convert never reads half a file.
            var tmp = $"{path}.{Environment.ProcessId}.tmp";
            File.WriteAllLines(tmp, lines.Prepend(DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture)).Prepend(stamp));
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    public static void Forget(string name) => Paths.TryDelete(Path.Combine(Folder, name));
}
