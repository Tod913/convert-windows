using System.Collections.Concurrent;
using ImageMagick;
using ImageMagick.Configuration;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Convert.Core;

public static class ImageConv
{
    public static MagickFormat FormatFor(string id) => id switch
    {
        "jpg" or "pdf-jpg" => MagickFormat.Jpeg,
        "png" or "pdf-png" => MagickFormat.Png,
        "heic" => MagickFormat.Heic,
        "avif" => MagickFormat.Avif,
        "webp" => MagickFormat.WebP,
        "tiff" => MagickFormat.Tiff,
        "gif-img" => MagickFormat.Gif,
        "bmp" => MagickFormat.Bmp,
        "pdf-img" => MagickFormat.Pdf,
        _ => throw new ConvertException($"Unknown image format {id}"),
    };

    private static readonly ConcurrentDictionary<string, bool> writable = new();

    public static bool CanWrite(string id)
    {
        // Magick.NET reads HEIC but can't write it; Windows' own HEIF encoder can.
        if (id == "heic") return WindowsHeif.Available;
        return writable.GetOrAdd(id, static id =>
        {
            // Asked when a window for pictures opens, and asking means loading ImageMagick (a few tenths of a
            // second). The answer only changes with ImageMagick itself, so it's kept for this copy of it.
            var stamp = Tools.Stamp(typeof(MagickFormatInfo).Assembly.Location);
            var kept = Kept.Read("formats.txt", stamp) ?? [];
            if (kept.FirstOrDefault(line => line.StartsWith(id + "=", StringComparison.Ordinal)) is { } known) return known.EndsWith("=1", StringComparison.Ordinal);
            Setup();
            bool can;
            try { can = MagickFormatInfo.Create(FormatFor(id))?.SupportsWriting == true; }
            catch (Exception e) when (e is MagickException or ConvertException) { can = false; }
            lock (writable) Kept.Write("formats.txt", stamp, (Kept.Read("formats.txt", stamp) ?? []).Append($"{id}={(can ? 1 : 0)}").Distinct());
            return can;
        });
    }

    // Reading safely

    /// <summary>
    /// The decoder for each accepted extension. ImageMagick is always told which one to use: left
    /// to itself it goes by the name and then by the contents, and its list of formats includes ones
    /// that are really scripts (MSL, MVG) or that fetch things (URL).
    /// </summary>
    private static readonly Dictionary<string, MagickFormat> Decoders = new(StringComparer.OrdinalIgnoreCase)
    {
        ["jpg"] = MagickFormat.Jpeg,
        ["jpeg"] = MagickFormat.Jpeg,
        ["jpe"] = MagickFormat.Jpeg,
        ["jfif"] = MagickFormat.Jpeg,
        ["png"] = MagickFormat.Png,
        ["gif"] = MagickFormat.Gif,
        ["bmp"] = MagickFormat.Bmp,
        ["dib"] = MagickFormat.Dib,
        ["tif"] = MagickFormat.Tiff,
        ["tiff"] = MagickFormat.Tiff,
        ["webp"] = MagickFormat.WebP,
        ["heic"] = MagickFormat.Heic,
        ["heif"] = MagickFormat.Heif,
        ["hif"] = MagickFormat.Heic,
        ["avif"] = MagickFormat.Avif,
        ["ico"] = MagickFormat.Ico,
        ["jxl"] = MagickFormat.Jxl,
        ["psd"] = MagickFormat.Psd,
        ["tga"] = MagickFormat.Tga,
        ["svg"] = MagickFormat.Svg,
        ["dng"] = MagickFormat.Dng,
        ["cr2"] = MagickFormat.Cr2,
        ["cr3"] = MagickFormat.Cr3,
        ["nef"] = MagickFormat.Nef,
        ["nrw"] = MagickFormat.Nrw,
        ["arw"] = MagickFormat.Arw,
        ["orf"] = MagickFormat.Orf,
        ["rw2"] = MagickFormat.Rw2,
        ["raf"] = MagickFormat.Raf,
        ["pef"] = MagickFormat.Pef,
        ["srw"] = MagickFormat.Srw,
        ["erf"] = MagickFormat.Erf,
        ["3fr"] = MagickFormat.ThreeFr,
    };

    /// <summary>Whether there's a decoder for this extension (without the dot).</summary>
    public static bool CanRead(string extension) => Decoders.ContainsKey(extension);

    /// <summary>
    /// The decoder for a file: the one its contents call for when they're a well-known format
    /// (a PNG saved as .jpg still converts, as on the Mac), otherwise the one for its extension.
    /// </summary>
    private static MagickFormat? DecoderFor(string input, byte[] head)
    {
        MagickFormat? byName = Decoders.TryGetValue(Path.GetExtension(input).TrimStart('.'), out var f) ? f : null;
        MagickFormat? byContent = Sniff.FormatOf(head)?.Ext switch
        {
            "jpg" => MagickFormat.Jpeg,
            "png" => MagickFormat.Png,
            "gif" => MagickFormat.Gif,
            "bmp" => MagickFormat.Bmp,
            "webp" => MagickFormat.WebP,
            "heic" => MagickFormat.Heic,
            "avif" => MagickFormat.Avif,
            "tiff" => MagickFormat.Tiff,
            _ => null,
        };
        // Camera raw files start like TIFF files; their own decoder knows better.
        if (byContent == MagickFormat.Tiff && byName is not (null or MagickFormat.Jpeg or MagickFormat.Png or MagickFormat.Gif
                or MagickFormat.Bmp or MagickFormat.Dib or MagickFormat.WebP)) return byName;
        return byContent ?? byName;
    }

    private static readonly object setupGate = new();
    private static bool isSetUp;
    private static string? configuration;

    /// <summary>Folders of Convert processes that ended without tidying up (a crash, the power going).</summary>
    private static void RemoveLeftConfigurations(string root)
    {
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                if (!int.TryParse(Path.GetFileName(dir), out var pid)) continue;
                try { System.Diagnostics.Process.GetProcessById(pid).Dispose(); }
                catch (ArgumentException) { Paths.TryDelete(dir); }   // no such process any more
                catch (InvalidOperationException) { }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Removes the configuration folder <see cref="Setup"/> made for this process.</summary>
    public static void CleanUp() => Paths.TryDelete(Interlocked.Exchange(ref configuration, null));

    /// <summary>
    /// Limits what ImageMagick may do, once, before its first use: no scripting formats, nothing
    /// fetched, no helper programs, and a ceiling on memory and disk so that a small file claiming
    /// to be an enormous picture is refused instead of filling the PC.
    /// </summary>
    public static void Setup()
    {
        lock (setupGate)
        {
            if (isSetUp) return;
            isSetUp = true;
            var files = ConfigurationFiles.Default;
            files.Policy.Data = Policy;
            // ImageMagick reads its configuration from files. Each process writes its own, so one
            // Convert window never reads a folder another is in the middle of writing.
            var folder = Path.Combine(Paths.TempRoot, "magick", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            RemoveLeftConfigurations(Path.GetDirectoryName(folder)!);
            Directory.CreateDirectory(folder);
            MagickNET.Initialize(files, folder);
            configuration = folder;
            AppDomain.CurrentDomain.ProcessExit += (_, _) => CleanUp();

            // A quarter of the PC's memory for pixels; past that ImageMagick uses a temp file, up to the disk limit.
            ResourceLimits.LimitMemory(new Percentage(25));
            ResourceLimits.Disk = 8UL << 30;
            ResourceLimits.MaxMemoryRequest = 4UL << 30;
            // No real picture is wider or taller; JPEG stops at 65,535.
            ResourceLimits.Width = 100_000;
            ResourceLimits.Height = 100_000;
            ResourceLimits.ListLength = 64;
            // Resizing a picture doesn't need the graphics driver, and a driver is one more thing that can go wrong.
            OpenCL.IsEnabled = false;
        }
    }

    // ImageMagick allows everything that isn't denied, and the last matching rule wins.
    private const string Policy = """
        <?xml version="1.0" encoding="UTF-8"?>
        <policymap>
          <policy domain="delegate" rights="none" pattern="*"/>
          <policy domain="filter" rights="none" pattern="*"/>
          <policy domain="path" rights="none" pattern="@*"/>
          <policy domain="path" rights="none" pattern="|*"/>
          <policy domain="module" rights="none" pattern="{MSL,MVG,URL,EPHEMERAL,TEXT,LABEL,CAPTION,PANGO,SCRIPT,VID,SCR,CLIPBOARD,SCREENSHOT,MPR,INLINE,FTP,HTTP,HTTPS}"/>
          <policy domain="coder" rights="write" pattern="{PDF,EPDF,PS,PS2,PS3,EPS,EPSF,EPSI,EPT,AI,XPS,PCL}"/>
        </policymap>
        """;

    public static string Convert(string input, Target t, ConvSettings s, Runner runner)
    {
        runner.CheckCancelled();
        Setup();
        MagickImage img;
        try
        {
            if ((Sniff.Header(input) is { } head ? DecoderFor(input, head) : null) is not { } decoder)
                throw new ConvertException("Couldn't read this image");
            // Read from a stream, not by name: ImageMagick gives meaning to parts of a file name
            // ("photo[1].jpg" is a frame, "label:…" a made-up picture), and a name is just a name.
            using var file = new FileStream(input, FileMode.Open, FileAccess.Read, FileShare.Read);
            img = new MagickImage(file, new MagickReadSettings { Format = decoder, FrameIndex = 0, FrameCount = 1 });
        }
        catch (MagickException e) { throw new ConvertException($"Couldn't read this image ({FirstLine(e.Message)})"); }
        // An empty file, one that can't be opened, a number in it that doesn't fit: not a picture, at any rate.
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or OverflowException)
        {
            throw new ConvertException("Couldn't read this image");
        }

        using (img)
        {
            try
            {
                runner.CheckCancelled();
                img.AutoOrient();
                if (s.MaxSize > 0 && Math.Max(img.Width, img.Height) > s.MaxSize)
                    img.Resize(new MagickGeometry((uint)s.MaxSize, (uint)s.MaxSize));   // fits inside, keeps aspect
                if (t.Id is "jpg" or "bmp" && img.HasAlpha)
                {
                    img.BackgroundColor = MagickColors.White;
                    img.Alpha(AlphaAction.Remove);
                    img.Alpha(AlphaAction.Off);
                }
                if (t.IsLossyImage) img.Quality = (uint)Math.Round(Math.Clamp(s.ImageQuality, 0.05, 1) * 100);
                if (t.Id == "pdf-img") img.Strip();
            }
            catch (MagickException e) { throw new ConvertException($"Couldn't read this image ({FirstLine(e.Message)})"); }

            runner.CheckCancelled();
            var out_ = Paths.UniqueOutput(input, t.Ext);
            try
            {
                // Stopped or failed while writing: WriteNew removes what was written so far.
                Paths.WriteNew(out_, file =>
                {
                    if (t.Id == "heic") WindowsHeif.Write(img, file, s.ImageQuality);
                    else img.Write(file, FormatFor(t.Id));
                    runner.CheckCancelled();
                });
            }
            catch (MagickException e) { throw new ConvertException($"Couldn't write {t.Label} ({FirstLine(e.Message)})"); }
            catch (IOException) when (!Directory.Exists(Path.GetDirectoryName(out_)) || !OutputFolder.IsWritable(Path.GetDirectoryName(out_)!))
            {
                // A format this PC can write, but the file can't be created: that's the folder, not the format.
                throw new ConvertException($"Couldn't save to “{OutputFolder.Name(Path.GetDirectoryName(out_)!)}”: that folder can't be written to");
            }
            catch (Exception e) when (e is not (ConvertException or OperationCanceledException))
            {
                throw new ConvertException($"Couldn't write {t.Label} ({FirstLine(e.Message)})");
            }
            return out_;
        }
    }

    private static string FirstLine(string s) => s.Split('\n')[0].Trim();
}

/// <summary>
/// HEIC through Windows Imaging Component. Needs the "HEIF Image Extensions" and "HEVC Video Extensions"
/// from the Microsoft Store; without them the target is hidden.
/// </summary>
public static class WindowsHeif
{
    private static readonly Lazy<bool> available = new(() =>
    {
        try
        {
            // A small grey picture made here rather than by ImageMagick: this is asked when a window for
            // pictures opens, and loading ImageMagick just for this would slow every one of them down.
            // Asked anew each time: the Store extensions can be installed at any moment.
            var grey = new byte[64 * 64 * 4];
            Array.Fill(grey, (byte)128);
            using var encoded = new MemoryStream();
            Task.Run(() => EncodeAsync(grey, 64, 64, encoded, 0.5)).GetAwaiter().GetResult();
            return encoded.Length > 0;
        }
        catch (Exception) { return false; }
    });

    public static bool Available => available.Value;

    /// <summary>Encodes an already decoded, oriented and resized image as HEIC.</summary>
    public static void Write(MagickImage img, Stream output, double quality) =>
        // WinRT async calls block safely on the thread pool (never on a UI thread).
        Task.Run(() => WriteAsync(img, output, quality)).GetAwaiter().GetResult();

    private static async Task WriteAsync(MagickImage img, Stream output, double quality)
    {
        // Hand the pixels over as BGRA8, the format WIC's encoders take.
        var w = img.Width;
        var h = img.Height;
        byte[] pixels;
        using (var copy = (MagickImage)img.Clone())
        {
            copy.ColorSpace = ColorSpace.sRGB;
            copy.Alpha(AlphaAction.Set);
            pixels = copy.GetPixels().ToByteArray(PixelMapping.BGRA)
                     ?? throw new ConvertException("Couldn't read the image pixels");
        }
        await EncodeAsync(pixels, w, h, output, quality);
    }

    /// <summary>Encodes BGRA8 pixels (straight alpha) as HEIC.</summary>
    private static async Task EncodeAsync(byte[] pixels, uint w, uint h, Stream output, double quality)
    {
        using var stream = new InMemoryRandomAccessStream();
        var options = new BitmapPropertySet
        {
            ["ImageQuality"] = new BitmapTypedValue((float)Math.Clamp(quality, 0.05, 1), Windows.Foundation.PropertyType.Single),
        };
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.HeifEncoderId, stream, options);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, w, h, 96, 96, pixels);
        await encoder.FlushAsync();

        stream.Seek(0);
        await using var src = stream.AsStreamForRead();
        await src.CopyToAsync(output);
    }
}
