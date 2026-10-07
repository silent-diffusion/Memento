using Memento.Core.Tests.Fakes;
using Memento.Core.Transcripts;

namespace Memento.Core.Tests.Transcripts;

public sealed class CoverageCheckTests
{
    private static Dictionary<string, IReadOnlyList<(double Start, double End)>> Speech(params (double, double)[] regions) =>
        new() { ["mic"] = regions };

    [Fact]
    public void SpeechWithoutTranscriptForMoreThanTwentySecondsIsAGap()
    {
        // Speech from 10 to 60 s in short phrases; the transcript covers 10–25 and 50–60.
        var phrases = Enumerable.Range(0, 25).Select(i => (10.0 + (i * 2), 10.0 + (i * 2) + 1.6)).ToArray();
        var segments = new[] { TranscriptFixtures.Segment("s1", 10, 25, "First part."), TranscriptFixtures.Segment("s2", 50, 60, "Last part.") };

        var gap = Assert.Single(CoverageCheck.Find(Speech(phrases), segments));

        Assert.Equal("mic", gap.Track);
        Assert.InRange(gap.Start, 25, 26.5);
        Assert.InRange(gap.End, 48, 50);
    }

    [Fact]
    public void ShortGapsSilenceAndOtherTracksAreNotGaps()
    {
        var segments = new[]
        {
            TranscriptFixtures.Segment("s1", 0, 10, "One."),
            TranscriptFixtures.Segment("s2", 25, 40, "Two."),
            TranscriptFixtures.Segment("s3", 40, 90, "On the other track.", track: "system"),
        };

        // 10–25 is speech but only 15 s; 40–90 is silent on the mic.
        Assert.Empty(CoverageCheck.Find(Speech((0, 24), (25, 40)), segments));
    }

    [Fact]
    public void AStretchThatIsMostlySilenceIsNotAGap()
    {
        // 30 s span with only two 1 s blips of energy (a door, a cough).
        Assert.Empty(CoverageCheck.Find(Speech((100, 101), (101.5, 102.5), (128, 129)), []));
    }

    [Fact]
    public void ATrackWithSpeechAndNoTranscriptAtAllIsOneGap()
    {
        var gaps = CoverageCheck.Find(Speech((0, 30)), []);

        Assert.Equal((0, 30), (Assert.Single(gaps).Start, gaps[0].End));
    }
}
