using Memento.Core.Bridge.Contracts;

namespace Memento.Transcription.Tests;

public sealed class TranscriptMergerTests
{
    [Fact]
    public void TracksAreMergedByTimeAndNumbered()
    {
        var merged = TranscriptMerger.Merge(
        [
            new TranscriptSegment(string.Empty, 5, 6, "system", null, null, "b", 1, [], null),
            new TranscriptSegment(string.Empty, 1, 2, "mic", null, null, "a", 1, [], null),
            new TranscriptSegment(string.Empty, 5, 7, "app-zoom", null, null, "c", 1, [], null),
        ]);

        Assert.Equal(["s0001", "s0002", "s0003"], merged.Select(s => s.Id));
        Assert.Equal(["a", "c", "b"], merged.Select(s => s.Text));
        Assert.Equal(["mic", "app-zoom", "system"], merged.Select(s => s.Track));
    }
}
