namespace Convert.Core;

public static class Converter
{
    /// <summary>
    /// Converts one file and returns what was written (files, or a folder for multi-page PDFs and font
    /// collections). Stopped (<see cref="Runner.Cancel"/>) or failed conversions throw and leave
    /// nothing half-written behind.
    /// </summary>
    public static async Task<List<string>> ConvertAsync(string input, Target t, ConvSettings s, Runner runner, Action<double> progress)
    {
        runner.CheckCancelled();
        if (!File.Exists(input)) throw new ConvertException("The file no longer exists");
        // A file whose contents don't match its name gets told so, instead of a vague decode error.
        if (Sniff.KindOf(input) is { } kind) Sniff.Verify(input, kind);
        switch (t.Group)
        {
            case OutGroup.Audio:
            case OutGroup.Video:
                {
                    var ff = Tools.FFmpeg ?? throw new ConvertException("ffmpeg isn't installed. Install it with: winget install Gyan.FFmpeg.Essentials");
                    var p = await Task.Run(() => FF.ProbeFile(input));
                    if (p.Ok && t.Group == OutGroup.Audio && !p.HasAudio) throw new ConvertException("This file has no audio track");
                    if (p.Ok && t.Group == OutGroup.Video && !p.HasVideo) throw new ConvertException("This file has no video track");
                    // Without a working GPU encoder there's nothing to fall back from.
                    var hardware = s.Hardware && t.HasHardwareOption && await Task.Run(() => HardwareEncoder.Vendor) != null;
                    var out_ = Paths.UniqueOutput(input, t.Ext);
                    await WithSoftwareFallback(t, s with { Hardware = hardware }, runner, async settings =>
                    {
                        var codec = t.Group == OutGroup.Audio ? FF.AudioArgs(t, settings, p) : FF.VideoArgs(t, settings, p);
                        try
                        {
                            await runner.RunAsync(ff, [.. FF.Common, "-i", input, .. codec, out_], p.Duration, progress);
                        }
                        catch
                        {
                            Paths.TryDelete(out_);
                            throw;
                        }
                    });
                    return [out_];
                }
            case OutGroup.Image:
                return [await Task.Run(() => ImageConv.Convert(input, t, s, runner))];
            case OutGroup.Document:
                return [await DocConv.ConvertAsync(input, t, s, runner)];
            case OutGroup.PdfPages:
                return await PdfConv.ConvertAsync(input, t, s, runner, progress);
            case OutGroup.Font:
                return await Task.Run(() => FontConv.Convert(input, t, runner));
            default:
                throw new ConvertException("Unsupported target");
        }
    }

    /// <summary>
    /// Formats with a hardware encoder use it by default, but a GPU encoder that passed the quick
    /// check at startup can still refuse the real job (a resolution or pixel format it doesn't do,
    /// a driver in a bad mood, too many sessions), and ffmpeg then fails. Rather than fail the
    /// conversion, it's tried once more with the software encoder: slower, but it works anywhere.
    /// </summary>
    public static async Task WithSoftwareFallback(Target t, ConvSettings s, Runner runner, Func<ConvSettings, Task> encode)
    {
        try
        {
            await encode(s);
        }
        catch (Exception) when (t.HasHardwareOption && s.Hardware && !runner.IsCancelled)
        {
            HardwareEncoder.Forget();
            await encode(s with { Hardware = false });
        }
    }
}
