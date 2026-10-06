using Memento.Core.Recording;
using Memento.Core.Tests.Fakes;

namespace Memento.Core.Tests.Recording;

public sealed class CapturePartsTests : IDisposable
{
    private readonly TempDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    [Theory]
    [InlineData("tracks/mic.wav", 1, "tracks/mic.wav")]
    [InlineData("tracks/mic.wav", 2, "tracks/mic.part2.wav")]
    [InlineData("tracks/app-zoom-2.wav", 11, "tracks/app-zoom-2.part11.wav")]
    [InlineData("tracks\\system.wav", 3, "tracks/system.part3.wav")]
    [InlineData("mix.wav", 2, "mix.part2.wav")]
    public void PartPathsFollowTheRolloverConvention(string first, int index, string expected) =>
        Assert.Equal(expected, CaptureParts.PartPath(first, index));

    [Fact]
    public void FindListsPartsInOrderAndStopsAtTheFirstGap()
    {
        Directory.CreateDirectory(_directory.File("tracks"));
        File.WriteAllText(_directory.File("tracks/mic.wav"), "1");
        File.WriteAllText(_directory.File("tracks/mic.part2.wav"), "2");
        File.WriteAllText(_directory.File("tracks/mic.part4.wav"), "4");

        Assert.Equal(["tracks/mic.wav", "tracks/mic.part2.wav"], CaptureParts.Find(_directory.Path, "tracks/mic.wav"));
        Assert.Empty(CaptureParts.Find(_directory.Path, "tracks/system.wav"));
    }
}
