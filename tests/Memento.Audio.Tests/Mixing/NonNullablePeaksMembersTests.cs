using Memento.Audio.Mixing;
using Memento.Core.Tests.Serialization;

namespace Memento.Audio.Tests.Mixing;

/// <summary>Security audit 2, §9, for <c>peaks.json</c>: a list or object member the JSON leaves out keeps its default.</summary>
public sealed class NonNullablePeaksMembersTests
{
    [Fact]
    public void NoListOrObjectOfThePeaksFileReadsAsNullWhenTheJsonLeavesItOut()
    {
        var problems = NonNullableContracts.FindNullsReadFromEmptyObjects(AudioJsonContext.Default);

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
        Assert.NotEmpty(NonNullableContracts.ObjectTypes(AudioJsonContext.Default));
    }

    [Theory]
    [InlineData("""{ "schemaVersion": 1, "windowMs": 50 }""", 0)]
    [InlineData("""{ "schemaVersion": 1, "windowMs": 50, "peaks": null }""", 0)]
    [InlineData("""{ "schemaVersion": 1, "windowMs": 50, "peaks": [null, [0.1, 0.2]] }""", 1)]
    public async Task APeaksFileWithANullListOrWindowReadsWithoutIt(string json, int windows)
    {
        using var directory = new TempDirectory();
        var path = directory.File("peaks.json");
        await File.WriteAllTextAsync(path, json);

        var peaks = await PeakBuilder.ReadAsync(path, CancellationToken.None);

        Assert.Equal(windows, peaks.Peaks.Count);
        Assert.All(peaks.Peaks, window => Assert.NotNull(window));
    }
}
