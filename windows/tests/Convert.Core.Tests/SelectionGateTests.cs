using Convert.Core;
using Xunit;

namespace Convert.Core.Tests;

/// <summary>
/// The classic right-click menu starts one Convert per selected file; the first opens the window and
/// the others hand their files to it (<see cref="SelectionGate"/>).
/// </summary>
public sealed class SelectionGateTests
{
    private static string NewName() => $"Convert.Test.{Guid.NewGuid():N}";

    [Fact]
    public async Task TheFirstOpensTheWindowAndTheOthersHandTheirFilesToIt()
    {
        var name = NewName();
        using var gate = SelectionGate.Open([@"C:\a.mp3"], name);
        Assert.NotNull(gate);
        var gathered = new List<string>();
        gate.Listen(more => { lock (gathered) gathered.AddRange(more); return true; });

        // Like Explorer: the others start at about the same time.
        var others = Enumerable.Range(1, 8).Select(i => Task.Run(() => SelectionGate.Open([$@"C:\file {i} – “x”.mp3"], name))).ToArray();
        foreach (var other in await Task.WhenAll(others)) Assert.Null(other);

        await gate.Completed.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(Enumerable.Range(1, 8).Select(i => $@"C:\file {i} – “x”.mp3").Order(), gathered.Order());
    }

    [Fact]
    public async Task ABusyWindowRefusesAndTheNextSelectionOpensItsOwn()
    {
        var name = NewName();
        using var gate = SelectionGate.Open([@"C:\a.mp3"], name);
        Assert.NotNull(gate);
        gate.Listen(_ => false);

        using var own = SelectionGate.Open([@"C:\b.mp3"], name);
        Assert.NotNull(own);
        await gate.Completed.WaitAsync(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task AfterTheWindowStopsListeningANewSelectionGetsItsOwnWindow()
    {
        var name = NewName();
        var gate = SelectionGate.Open([@"C:\a.mp3"], name);
        Assert.NotNull(gate);
        gate.Listen(_ => true);
        gate.Dispose();
        await gate.Completed.WaitAsync(TimeSpan.FromSeconds(30));

        using var next = SelectionGate.Open([@"C:\b.mp3"], name);
        Assert.NotNull(next);
    }

    [Fact]
    public async Task OnlyFullPathsAreTaken()
    {
        var name = NewName();
        using var gate = SelectionGate.Open([@"C:\a.mp3"], name);
        Assert.NotNull(gate);
        var gathered = new List<string>();
        gate.Listen(more => { lock (gathered) gathered.AddRange(more); return true; });

        // A relative path would mean the folder Convert happens to run in: refused, so that process opens its own.
        using var own = SelectionGate.Open(["b.mp3"], name);
        Assert.NotNull(own);
        await gate.Completed.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Empty(gathered);
    }
}
