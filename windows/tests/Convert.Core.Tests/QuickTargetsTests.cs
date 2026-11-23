using Convert.Core;
using Xunit;

namespace Convert.Core.Tests;

/// <summary>The formats under "Convert to" in the classic right-click menu.</summary>
public sealed class QuickTargetsTests
{
    private static List<string> Ids(string ext) => QuickTargets.For(ext, Targets.Catalogue).Select(t => t.Id).ToList();

    [Fact]
    public void EachKindGetsAFewCommonFormats()
    {
        Assert.Equal(["mp3", "gif"], Ids("mp4"));
        Assert.Equal(["mp4-h264", "mp3", "gif"], Ids("mov"));
        Assert.Equal(["m4a", "wav", "flac"], Ids("mp3"));
        Assert.Equal(["png", "webp", "pdf-img"], Ids("jpg"));
        Assert.Equal(["pdf-docx", "pdf-png", "pdf-jpg", "pdf-txt"], Ids("pdf"));
        Assert.Equal(["pdf-doc"], Ids("docx"));
        Assert.Equal(["font-woff2"], Ids("ttf"));
    }

    [Fact]
    public void TheFormatAFileIsAlreadyInIsLeftOutWhateverItsSpelling()
    {
        Assert.DoesNotContain("jpg", Ids("jpeg"));
        Assert.DoesNotContain("jpg", Ids("JFIF"));
        Assert.DoesNotContain("wav", Ids("wave"));
        Assert.DoesNotContain("font-desktop", Ids("otf"));
        Assert.Contains("font-desktop", Ids("woff2"));
    }

    [Fact]
    public void OnlyFormatsThisPcCanWriteAreOffered()
    {
        var withoutWebp = Targets.Catalogue.Where(t => t.Id != "webp").ToList();
        Assert.Equal(["jpg", "pdf-img"], QuickTargets.For("png", withoutWebp).Select(t => t.Id));
    }

    [Fact]
    public void EveryQuickFormatExistsAndUnknownFilesGetNone()
    {
        foreach (var ext in MediaKinds.AllExtensions)
            Assert.All(QuickTargets.For(ext, Targets.Catalogue), t => Assert.Contains(t, Targets.Catalogue));
        Assert.Empty(Ids("exe"));
        Assert.All(MediaKinds.AllExtensions, ext => Assert.NotEmpty(Ids(ext)));
    }
}
