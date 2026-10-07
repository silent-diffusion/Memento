using Memento.Core.Tests.Fakes;
using Memento.Core.Transcripts;

namespace Memento.Core.Tests.Transcripts;

public sealed class CoverageCheckTests
{
    private static Dictionary<string, IReadOnlyList<(double Start, double End)>> Speech(params (double, double)[] regions) =>
        new() { ["mic"] = regions };

    [Fact]
    public void SpeechWithoutTranscriptForTwentyFiveSecondsIsAGap()
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
    public void AFifteenSecondPassageTheEngineDroppedIsAGap()
    {
        // The passage large-v3-turbo dropped twice in testing: 15 s of speech between two lines.
        var segments = new[] { TranscriptFixtures.Segment("s1", 0, 3, "One."), TranscriptFixtures.Segment("s2", 18, 30, "Two.") };

        var gap = Assert.Single(CoverageCheck.Find(Speech((0, 30)), segments));

        Assert.Equal((3.5, 17.5), (gap.Start, gap.End));
    }

    [Fact]
    public void ShortGapsSilenceAndOtherTracksAreNotGaps()
    {
        var segments = new[]
        {
            TranscriptFixtures.Segment("s1", 0, 10, "One."),
            TranscriptFixtures.Segment("s2", 19, 40, "Two."),
            TranscriptFixtures.Segment("s3", 40, 90, "On the other track.", track: "system"),
        };

        // 10–19 is speech but under 10 s once the half-second slack on both lines is taken off; 40–90 is silent on the mic.
        Assert.Empty(CoverageCheck.Find(Speech((0, 18), (19, 40)), segments));
        Assert.Equal(CoverageCheck.MinGapSeconds, 10);
    }

    [Fact]
    public void AStretchThatIsMostlySilenceIsNotAGap()
    {
        // 30 s with only three 1 s blips of energy (a door, a cough), and 14 s of blips that are less than half sound.
        Assert.Empty(CoverageCheck.Find(Speech((100, 101), (101.5, 102.5), (128, 129)), []));
        Assert.Empty(CoverageCheck.Find(Speech((200, 201), (202.5, 203.5), (205, 206), (207.5, 208.5), (210, 211), (212.5, 213.5), (214, 214.2)), []));
    }

    [Fact]
    public void ATrackWithSpeechAndNoTranscriptAtAllIsOneGap()
    {
        var gaps = CoverageCheck.Find(Speech((0, 30)), []);

        Assert.Equal((0, 30), (Assert.Single(gaps).Start, gaps[0].End));
    }
}
