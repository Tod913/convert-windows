using System.Text.Json;
using System.Text.Json.Serialization;

namespace Convert.Core;

// Mirrors Sources/Convert/Formats.swift: same target IDs, order, defaults and option ranges.

public enum OutGroup { Video, Audio, Image, Document, PdfPages, Font }

public static class OutGroups
{
    public static string Label(this OutGroup g) => g switch
    {
        OutGroup.PdfPages => "From PDF",
        _ => g.ToString(),
    };

    public static IReadOnlySet<OutGroup> For(MediaKind k) => k switch
    {
        MediaKind.Audio => new HashSet<OutGroup> { OutGroup.Audio },
        MediaKind.Video => new HashSet<OutGroup> { OutGroup.Video, OutGroup.Audio },
        MediaKind.Image => new HashSet<OutGroup> { OutGroup.Image },
        MediaKind.Pdf => new HashSet<OutGroup> { OutGroup.PdfPages },
        MediaKind.Font => new HashSet<OutGroup> { OutGroup.Font },
        _ => new HashSet<OutGroup> { OutGroup.Document },
    };
}

public static class MediaKindText
{
    public static string Noun(this MediaKind k) => k switch
    {
        MediaKind.Pdf => "PDF",
        _ => k.ToString(),
    };

    /// <summary>Segoe Fluent Icons glyph for the kind.</summary>
    public static string Glyph(this MediaKind k) => k switch
    {
        MediaKind.Audio => "",
        MediaKind.Video => "",
        MediaKind.Image => "",
        MediaKind.Pdf => "",
        MediaKind.Font => "",
        _ => "",
    };
}

public sealed record Target(string Id, string Label, string Ext, OutGroup Group)
{
    public bool IsLossyAudio => Id is "mp3" or "m4a" or "he-aac" or "opus" or "ogg" or "wma" or "ac3" or "mp2";
    public bool IsLosslessAudio => Id is "wav" or "aiff" or "flac" or "alac" or "wv";
    public bool IsLossyImage => Id is "jpg" or "heic" or "avif" or "webp";
    /// <summary>ProRes has no hardware encoder on Windows, so unlike macOS it's not listed here.</summary>
    public bool HasHardwareOption => Id is "mp4-h264" or "mp4-hevc" or "mkv";

    public int[] BitrateOptions => Id switch
    {
        "mp3" => [0, 320, 256, 192, 160, 128, 96, 64],
        "m4a" => [320, 256, 192, 160, 128, 96],
        "he-aac" => [96, 80, 64, 48, 32],
        "opus" => [256, 192, 160, 128, 96, 64, 48, 32],
        "ogg" => [320, 256, 192, 160, 128, 96],
        "wma" => [256, 192, 160, 128, 96],
        "ac3" => [640, 448, 384, 256, 192],
        "mp2" => [384, 320, 256, 192, 128],
        _ => [],
    };

    public int DefaultBitrate => Id switch
    {
        "m4a" => 256,
        "he-aac" => 64,
        "opus" => 160,
        "ogg" or "wma" => 192,
        "ac3" => 448,
        "mp2" => 256,
        _ => 0,
    };

    public int[] SampleRates => Id == "opus" ? [] : IsLossyAudio ? [0, 44100, 48000] : [0, 44100, 48000, 88200, 96000];

    public bool HasChannelOption => Id != "ogg";

    /// <summary>PDF → a text format: the words and their fonts, without pictures or page layout.</summary>
    public bool IsPdfText => Id is "pdf-txt" or "pdf-docx" or "pdf-odt" or "pdf-rtf";

    /// <summary>Whether the window has anything to show under "Advanced" for this format.</summary>
    public bool HasSettings => !(Group == OutGroup.Document && Id != "pdf-doc") && !IsPdfText && Group != OutGroup.Font;

    public override string ToString() => Label;
}

public static class Targets
{
    /// <summary>Every format, in menu order, whether or not this PC can write it.</summary>
    public static readonly IReadOnlyList<Target> Catalogue = Build();

    /// <summary>
    /// Every format this PC can produce right now. A few depend on what's installed: HE-AAC on an
    /// ffmpeg built with libfdk_aac, HEIC on Windows' HEIF and HEVC extensions. That's checked on
    /// each use, so they show up as soon as what they need does.
    /// </summary>
    public static IReadOnlyList<Target> All => Catalogue.Where(IsAvailable).ToList();

    private static bool IsAvailable(Target t) => t.Id switch
    {
        "he-aac" => Tools.HasEncoder("libfdk_aac"),
        "heic" or "avif" or "webp" => ImageConv.CanWrite(t.Id),
        _ => true,
    };

    private static List<Target> Build()
    {
        static IEnumerable<Target> T(OutGroup g, params (string id, string label, string ext)[] list) =>
            list.Select(x => new Target(x.id, x.label, x.ext, g));

        return
        [
            .. T(OutGroup.Video,
                ("mp4-h264", "MP4 (H.264)", "mp4"),
                ("mp4-hevc", "MP4 (HEVC / H.265)", "mp4"),
                ("mov-prores", "MOV (ProRes 422)", "mov"),
                ("webm", "WebM (VP9)", "webm"),
                ("mkv", "MKV (H.264)", "mkv"),
                ("mp4-av1", "MP4 (AV1)", "mp4"),
                ("gif", "GIF (animated)", "gif")),
            .. T(OutGroup.Audio,
                ("mp3", "MP3", "mp3"),
                ("m4a", "M4A (AAC)", "m4a"),
                ("he-aac", "M4A (HE-AAC, small)", "m4a"),
                ("wav", "WAV", "wav"),
                ("aiff", "AIFF", "aiff"),
                ("flac", "FLAC", "flac"),
                ("alac", "ALAC (Apple Lossless)", "m4a"),
                ("opus", "Opus", "opus"),
                ("ogg", "OGG (Vorbis)", "ogg"),
                ("wma", "WMA", "wma"),
                ("ac3", "AC3 (Dolby Digital)", "ac3"),
                ("wv", "WavPack", "wv"),
                ("mp2", "MP2", "mp2")),
            .. T(OutGroup.Image,
                ("jpg", "JPEG", "jpg"),
                ("png", "PNG", "png"),
                ("heic", "HEIC", "heic"),
                ("avif", "AVIF", "avif"),
                ("webp", "WebP", "webp"),
                ("tiff", "TIFF", "tiff"),
                ("gif-img", "GIF", "gif"),
                ("bmp", "BMP", "bmp"),
                ("pdf-img", "PDF", "pdf")),
            .. T(OutGroup.Document,
                ("pdf-doc", "PDF", "pdf"),
                ("docx", "Word (DOCX)", "docx"),
                // The Mac app says "97–2004", after Word 2004 for Mac. On Windows that format has
                // always been called "Word 97–2003", and the Mac's label would read like a mistake.
                ("doc", "Word 97–2003 (DOC)", "doc"),
                ("odt", "OpenDocument (ODT)", "odt"),
                ("rtf", "Rich Text (RTF)", "rtf"),
                ("txt", "Plain Text", "txt"),
                ("html", "HTML", "html")),
            .. T(OutGroup.PdfPages,
                ("pdf-png", "PNG (one per page)", "png"),
                ("pdf-jpg", "JPEG (one per page)", "jpg"),
                ("pdf-docx", "Word (DOCX, text only)", "docx"),
                ("pdf-odt", "OpenDocument (ODT, text only)", "odt"),
                ("pdf-rtf", "Rich Text (RTF, text only)", "rtf"),
                ("pdf-txt", "Plain Text", "txt")),
            .. T(OutGroup.Font,
                ("font-woff2", "WOFF2 (web font)", "woff2"),
                ("font-woff", "WOFF (web font)", "woff"),
                // .ttf or .otf, whichever the font inside is.
                ("font-desktop", "TTF / OTF (desktop font)", "ttf")),
        ];
    }

    public static Target? ById(string id) => Catalogue.FirstOrDefault(t => t.Id == id) is { } t && IsAvailable(t) ? t : null;

    /// <summary>Output groups valid for every selected kind, in display order.</summary>
    public static List<(OutGroup Group, List<Target> Targets)> Groups(IReadOnlySet<MediaKind> kinds)
    {
        if (kinds.Count == 0) return [];
        var allowed = new HashSet<OutGroup>(Enum.GetValues<OutGroup>());
        foreach (var k in kinds) allowed.IntersectWith(OutGroups.For(k));
        // Only the formats these files can become are checked: asking whether HEIC can be written loads
        // the image engine, and asking ffmpeg for its encoders starts it, so an MP3 does neither for images.
        return Enum.GetValues<OutGroup>().Where(allowed.Contains)
                   .Select(g => (g, Catalogue.Where(t => t.Group == g && IsAvailable(t)).ToList())).ToList();
    }

    /// <summary>Sensible first choice for a selection, skipping the format the files are already in.</summary>
    public static Target? Preferred(IReadOnlySet<MediaKind> kinds, IReadOnlySet<string> sourceExts, IReadOnlyList<Target> available)
    {
        MediaKind? k = kinds.Count == 1 ? kinds.First()
            : kinds.SetEquals([MediaKind.Audio, MediaKind.Video]) ? MediaKind.Audio : null;
        string[] order = k switch
        {
            MediaKind.Audio => ["mp3", "wav", "m4a"],
            MediaKind.Video => ["mp4-h264", "mov-prores"],
            MediaKind.Image => ["jpg", "png"],
            MediaKind.Pdf => ["pdf-png"],
            MediaKind.Document => ["pdf-doc", "docx"],
            MediaKind.Font => ["font-woff2", "font-desktop"],
            _ => [],
        };
        foreach (var id in order)
        {
            var t = available.FirstOrDefault(x => x.Id == id);
            if (t != null && !sourceExts.Contains(t.Ext)) return t;
        }
        return available.FirstOrDefault(t => !sourceExts.Contains(t.Ext)) ?? available.FirstOrDefault();
    }
}

/// <summary>
/// The settings of one conversion. Saved per format as JSON, under the same names as on the Mac.
/// A key that's missing keeps its default, so settings saved by an older version survive an update
/// that adds a field; unknown keys are ignored.
/// </summary>
public sealed record ConvSettings
{
    // Audio
    public int AudioBitrate { get; set; }          // kbps; 0 = MP3 VBR best
    public int SampleRate { get; set; }            // 0 = same as source
    public int Channels { get; set; }              // 0 = same, 1 = mono, 2 = stereo
    public int BitDepth { get; set; }              // 0 = auto (match source), 16, 24
    // Video
    public double VideoQuality { get; set; } = 0.65; // 0 = smallest … 1 = best
    public int VideoHeight { get; set; }           // 0 = same as source
    public int Fps { get; set; }                   // 0 = same as source
    public bool KeepAudio { get; set; } = true;
    public bool Hardware { get; set; } = true;
    public int GifWidth { get; set; } = 480;
    public int GifFps { get; set; } = 15;
    // Image
    public double ImageQuality { get; set; } = 0.85;
    public int MaxSize { get; set; }               // longest edge in px; 0 = same as source
    // Documents / PDF
    public bool PaperA4 { get; set; } = true;
    [JsonPropertyName("pdfDPI")]
    public int PdfDpi { get; set; } = 150;

    public ConvSettings Copy() => this with { };

    /// <summary>
    /// How settings are stored: the Mac's key names. Reading ignores case, so files written by the
    /// first Windows version ("AudioBitrate") still load.
    /// </summary>
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    /// <summary>
    /// The default settings with the keys from a JSON object replaced, so callers only name what they
    /// change (<c>CONVERT_SETTINGS</c>). A name that isn't a setting is an error: a typo shouldn't
    /// silently convert with the defaults.
    /// </summary>
    public static ConvSettings WithOverrides(string json)
    {
        var known = JsonSerializer.SerializeToElement(new ConvSettings(), Json).EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToList();
        JsonDocument overrides;
        try { overrides = JsonDocument.Parse(json); }
        catch (JsonException) { throw new ConvertException("expected a JSON object such as {\"hardware\": false}"); }
        using (overrides)
        {
            if (overrides.RootElement.ValueKind != JsonValueKind.Object)
                throw new ConvertException("expected a JSON object such as {\"hardware\": false}");
            foreach (var key in overrides.RootElement.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal))
            {
                if (!known.Contains(key)) throw new ConvertException($"unknown setting \"{key}\"; known: {string.Join(", ", known)}");
            }
            try { return overrides.RootElement.Deserialize<ConvSettings>(Json) ?? new ConvSettings(); }
            catch (JsonException e) { throw new ConvertException(e.Message); }
        }
    }
}

public sealed class ConvertException(string message) : Exception(message);
