namespace Convert.Core;

/// <summary>
/// The formats under "Convert to" in Explorer's classic right-click menu: a few common ones per kind of
/// file, which convert at once with the saved settings. Everything else is in the window (Convert…).
/// </summary>
public static class QuickTargets
{
    private static readonly IReadOnlyDictionary<MediaKind, string[]> Ids = new Dictionary<MediaKind, string[]>
    {
        [MediaKind.Audio] = ["mp3", "m4a", "wav", "flac"],
        [MediaKind.Video] = ["mp4-h264", "mp3", "gif"],
        [MediaKind.Image] = ["jpg", "png", "webp", "pdf-img"],
        [MediaKind.Pdf] = ["pdf-docx", "pdf-png", "pdf-jpg", "pdf-txt"],
        [MediaKind.Document] = ["pdf-doc"],
        [MediaKind.Font] = ["font-woff2", "font-desktop"],
    };

    /// <summary>Other spellings of the same format, so a .jpeg isn't offered "JPEG".</summary>
    private static readonly Dictionary<string, string> SameAs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["jpeg"] = "jpg", ["jpe"] = "jpg", ["jfif"] = "jpg", ["tif"] = "tiff", ["wave"] = "wav", ["aif"] = "aiff",
        ["htm"] = "html", ["otf"] = "ttf",
    };

    /// <summary>
    /// The quick formats for files with extension <paramref name="ext"/> (without the dot), in menu order:
    /// those in <paramref name="available"/>, except the format the file is already in.
    /// </summary>
    public static IReadOnlyList<Target> For(string ext, IReadOnlyList<Target> available)
    {
        if (MediaKinds.Of("file." + ext) is not { } kind) return [];
        var own = SameAs.GetValueOrDefault(ext, ext.ToLowerInvariant());
        return Ids[kind].Select(id => available.FirstOrDefault(t => t.Id == id))
                        .OfType<Target>()
                        .Where(t => !string.Equals(t.Ext, own, StringComparison.OrdinalIgnoreCase))
                        .ToList();
    }
}
