using Memento.Core.Bridge.Contracts;

namespace Memento.Transcription.Tests;

public sealed class RepeatFilterTests
{
    private static TranscriptSegment Segment(double start, string text, string track = "mic") =>
        new(string.Empty, start, start + 1.5, track, null, null, text, 0.9, [], null);

    [Fact]
    public void ALineTheEngineRepeatsThreeOrMoreTimesKeepsOnlyTheFirst()
    {
        var segments = new[]
        {
            Segment(0, "We approve the budget."),
            Segment(2, "Thank you."),
            Segment(4, "thank you"),
            Segment(6, "  Thank   you. "),
            Segment(8, "Thank you!"),
            Segment(10, "Next item."),
        };

        var result = RepeatFilter.Apply(segments);

        Assert.Equal(["We approve the budget.", "Thank you.", "Next item."], result.Segments.Select(s => s.Text));
        var run = Assert.Single(result.Runs);
        Assert.Equal((2.0, 3, 9.5), (run.Kept.Start, run.Dropped, run.End));
        Assert.Equal(3, result.DroppedCount);
    }

    [Fact]
    public void TwoIdenticalLinesInARowAreSpeechNotALoop()
    {
        var segments = new[] { Segment(0, "Yes."), Segment(1.6, "Yes."), Segment(3.2, "No.") };

        var result = RepeatFilter.Apply(segments);

        Assert.Same(segments, result.Segments);
        Assert.Empty(result.Runs);
    }

    [Fact]
    public void RunsAreFoundPerTrackAndTheInputOrderIsKept()
    {
        // The same words on the microphone and the system track are two people, not a loop; interleaved by time.
        var segments = new[]
        {
            Segment(0, "Okay.", "mic"), Segment(0.1, "Okay.", "system"),
            Segment(2, "Okay.", "mic"), Segment(2.1, "Something else.", "system"),
            Segment(4, "Okay.", "mic"),
        };

        var result = RepeatFilter.Apply(segments);

        Assert.Equal([("mic", 0.0), ("system", 0.1), ("system", 2.1)], result.Segments.Select(s => (s.Track!, s.Start)));
        Assert.Equal(2, Assert.Single(result.Runs).Dropped);
    }

    [Fact]
    public void EmptyLinesAreNeverARun()
    {
        var segments = new[] { Segment(0, ""), Segment(2, " "), Segment(4, "...") };

        Assert.Empty(RepeatFilter.Apply(segments).Runs);
    }
}
