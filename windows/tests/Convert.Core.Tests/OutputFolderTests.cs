using Convert.Core;
using Xunit;

namespace Convert.Core.Tests;

/// <summary>
/// Where converted files go: next to the original, or the fallback folder (Downloads) when the
/// original's folder is read-only. The smoke test runs real conversions out of a read-only folder.
/// </summary>
public sealed class OutputFolderTests : IDisposable
{
    private readonly TestFolder root = new();
    private readonly string source;
    private readonly string fallback;

    public OutputFolderTests()
    {
        source = Directory.CreateDirectory(root.File("source")).FullName;
        fallback = Directory.CreateDirectory(root.File("fallback")).FullName;
    }

    public void Dispose()
    {
        TestAccess.SetWritable(source, true);
        TestAccess.SetWritable(fallback, true);
        root.Dispose();
    }

    private string Original(string name = "photo.png")
    {
        var path = Path.Combine(source, name);
        File.WriteAllText(path, "x");
        return path;
    }

    private static void Touch(string path) => File.WriteAllText(path, "x");

    // "Save to" a chosen folder. Async: OutputFolder.Use lasts for the async method it is set in.

    [Fact]
    public async Task OutputGoesToTheChosenFolder()
    {
        await Task.Yield();
        var input = Original();
        var chosen = Directory.CreateDirectory(root.File("chosen")).FullName;
        OutputFolder.Use(chosen);
        var out_ = Paths.UniqueOutput(input, "jpg", fallback: fallback);
        Assert.Equal(Path.Combine(chosen, "photo.jpg"), out_);
        Assert.False(OutputFolder.IsFallback(out_, input));
        // Two originals with the same name, from different folders, don't collide.
        Touch(out_);
        Assert.Equal("photo 2.jpg", Path.GetFileName(Paths.UniqueOutput(input, "jpg", fallback: fallback)));
        Assert.Equal(Path.Combine(chosen, "photo pages"), Paths.UniqueFolder(input, " pages", fallback: fallback));
        Assert.Equal(["photo.png"], Directory.GetFiles(source).Select(Path.GetFileName));
    }

    [Fact]
    public async Task AChosenFolderIsUsedEvenWhenTheOriginalsFolderIsReadOnly()
    {
        await Task.Yield();
        var input = Original();
        TestAccess.SetWritable(source, false);
        var chosen = Directory.CreateDirectory(root.File("chosen")).FullName;
        OutputFolder.Use(chosen);
        Assert.Equal(Path.Combine(chosen, "photo.jpg"), Paths.UniqueOutput(input, "jpg", fallback: fallback));
    }

    [Fact]
    public async Task AChosenFolderThatIsGoneOrReadOnlyIsAnErrorNotAFallback()
    {
        await Task.Yield();
        var input = Original();
        OutputFolder.Use(root.File("gone"));
        var gone = Assert.Throws<ConvertException>(() => Paths.UniqueOutput(input, "jpg", fallback: fallback));
        Assert.Contains("“gone”", gone.Message);

        var locked = Directory.CreateDirectory(root.File("locked")).FullName;
        TestAccess.SetWritable(locked, false);
        try
        {
            OutputFolder.Use(locked);
            var e = Assert.Throws<ConvertException>(() => Paths.UniqueOutput(input, "jpg", fallback: fallback));
            Assert.Contains("can't be written to", e.Message);
        }
        finally { TestAccess.SetWritable(locked, true); }
        Assert.Empty(Directory.GetFiles(fallback));
    }

    [Fact]
    public async Task TheChosenFolderEndsWithTheMethodThatSetIt()
    {
        var input = Original();
        await Task.Run(async () =>
        {
            await Task.Yield();
            OutputFolder.Use(root.File("elsewhere"));
        });
        Assert.Null(OutputFolder.Chosen);
        Assert.Equal(Path.Combine(source, "photo.jpg"), Paths.UniqueOutput(input, "jpg", fallback: fallback));
    }

    [Fact]
    public void OutputGoesNextToTheOriginal()
    {
        var input = Original();
        var out_ = Paths.UniqueOutput(input, "jpg", fallback: fallback);
        Assert.Equal(Path.Combine(source, "photo.jpg"), out_);
        Assert.False(OutputFolder.IsFallback(out_, input));
        // A taken name gets a number.
        Touch(out_);
        Assert.Equal("photo 2.jpg", Path.GetFileName(Paths.UniqueOutput(input, "jpg", fallback: fallback)));
        // Looking for a place to write leaves nothing behind.
        Assert.Equal(["photo.jpg", "photo.png"], Directory.GetFiles(source).Select(Path.GetFileName).Order());
    }

    [Fact]
    public void ReadOnlyFolderFallsBack()
    {
        var input = Original();
        TestAccess.SetWritable(source, false);
        var out_ = Paths.UniqueOutput(input, "jpg", fallback: fallback);
        Assert.Equal(Path.Combine(fallback, "photo.jpg"), out_);
        Assert.True(OutputFolder.IsFallback(out_, input));
        // The same "name 2.ext" rule applies in the fallback folder.
        Touch(out_);
        Touch(Path.Combine(fallback, "photo 2.jpg"));
        Assert.Equal("photo 3.jpg", Path.GetFileName(Paths.UniqueOutput(input, "jpg", fallback: fallback)));
    }

    [Fact]
    public void TheReadOnlyAttributeAloneDoesNotMakeAFolderReadOnly()
    {
        // Windows sets it on folders for its own purposes (a custom icon); files can still be created.
        var input = Original();
        new DirectoryInfo(source).Attributes |= FileAttributes.ReadOnly;
        try
        {
            Assert.Equal(Path.Combine(source, "photo.jpg"), Paths.UniqueOutput(input, "jpg", fallback: fallback));
        }
        finally { new DirectoryInfo(source).Attributes &= ~FileAttributes.ReadOnly; }
    }

    [Fact]
    public void PageFoldersFallBackToo()
    {
        var input = Original("scan.pdf");
        TestAccess.SetWritable(source, false);
        var folder = Paths.UniqueFolder(input, " pages", fallback);
        Assert.Equal(Path.Combine(fallback, "scan pages"), folder);
        Directory.CreateDirectory(folder);
        Assert.Equal("scan pages 2", Path.GetFileName(Paths.UniqueFolder(input, " pages", fallback)));
    }

    [Fact]
    public void ErrorNamesThePermissionProblemWhenNothingIsWritable()
    {
        var input = Original();
        TestAccess.SetWritable(source, false);
        TestAccess.SetWritable(fallback, false);
        var error = Assert.Throws<ConvertException>(() => Paths.UniqueOutput(input, "jpg", fallback: fallback));
        Assert.Contains("read-only", error.Message);
        Assert.Contains("fallback", error.Message);
        Assert.DoesNotContain("JPEG", error.Message);
    }

    [Fact]
    public async Task AConversionOutOfAReadOnlyFolderLandsInTheFallbackFolder()
    {
        var input = Path.Combine(source, "notes.md");
        File.WriteAllText(input, "# Notes\n\nSome text.\n");
        TestAccess.SetWritable(source, false);
        Assert.Equal("Downloads", Path.GetFileName(OutputFolder.Fallback), ignoreCase: true);
        // CONVERT_FALLBACK_DIR is how the smoke test points the app at a folder of its own. No other
        // test converts out of a read-only folder, so setting it for a moment disturbs nothing.
        Environment.SetEnvironmentVariable("CONVERT_FALLBACK_DIR", fallback);
        try
        {
            Assert.Equal(fallback, OutputFolder.Fallback);
            var made = await Converter.ConvertAsync(input, Targets.ById("html")!, new ConvSettings(), new Runner(), _ => { });
            Assert.Equal(Path.Combine(fallback, "notes.html"), made.Single());
            Assert.Contains("<h1", File.ReadAllText(made.Single()));
            Assert.True(OutputFolder.IsFallback(made.Single(), input));
            // An original whose conversion went elsewhere stays where it is.
            Assert.False(FileOps.MayRecycle(input, made, stopped: false));
            Assert.Equal(["notes.md"], Directory.GetFiles(source).Select(Path.GetFileName));
        }
        finally { Environment.SetEnvironmentVariable("CONVERT_FALLBACK_DIR", null); }
    }

    // Move original to Recycle Bin

    [Fact]
    public void OriginalIsOnlyRecycledWhenItsOutputSitsNextToIt()
    {
        var input = Original();
        var beside = Path.Combine(source, "photo.jpg");
        Touch(beside);
        Assert.True(FileOps.MayRecycle(input, [beside], stopped: false));

        // Saved to the fallback folder: the original stays.
        var elsewhere = Path.Combine(fallback, "photo.jpg");
        Touch(elsewhere);
        Assert.False(FileOps.MayRecycle(input, [elsewhere], stopped: false));

        // Stopped, nothing written, output missing, or output is the original itself: never.
        Assert.False(FileOps.MayRecycle(input, [beside], stopped: true));
        Assert.False(FileOps.MayRecycle(input, [], stopped: false));
        Assert.False(FileOps.MayRecycle(input, [Path.Combine(source, "gone.jpg")], stopped: false));
        Assert.False(FileOps.MayRecycle(input, [input], stopped: false));
        Assert.False(FileOps.MayRecycle(input, [input.ToUpperInvariant()], stopped: false));
    }
}
