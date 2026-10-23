using Memento.Core.Bridge.Contracts;

namespace Memento.Transcription.Tests;

public sealed class BleedFilterTests
{
    private static readonly IReadOnlyDictionary<string, string> Kinds = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["mic"] = BleedFilter.MicrophoneKind,
        ["mic2"] = BleedFilter.MicrophoneKind,
        ["system"] = BleedFilter.SystemKind,
        ["teams"] = BleedFilter.ApplicationKind,
    };

    private static TranscriptSegment Segment(string track, double start, double end, string text, TranscriptEdit? edited = null) =>
        new(string.Empty, start, end, track, null, null, text, 0.9, [], edited);

    [Fact]
    public void AMicrophoneLineThatRepeatsASystemLineAtTheSameTimeIsLeftOut()
    {
        var system = Segment("system", 10.0, 13.0, "So the budget for next year is approved.");
        var mic = Segment("mic", 10.2, 13.1, "so the budget for next year is approved");
        var segments = new[] { system, mic, Segment("mic", 14.0, 15.5, "Great, thanks everyone for coming.") };

        var result = BleedFilter.Apply(segments, Kinds);

        Assert.Equal([system.Text, "Great, thanks everyone for coming."], result.Segments.Select(s => s.Text));
        var dropped = Assert.Single(result.Dropped);
        Assert.Same(mic, dropped.Line);
        Assert.Same(system, dropped.KeptBy);
        Assert.Equal((1, 10.2, 13.1), (result.DroppedCount, result.Start, result.End));
    }

    [Fact]
    public void TheMicrophoneCopyMayBeDegradedAndStillCounts()
    {
        // The room microphone hears a muffled copy: fewer words, an accent lost, punctuation gone.
        var system = Segment("system", 0, 4, "Café prices went up by twelve percent, didn't they?");
        var mic = Segment("mic", 0.3, 3.9, "cafe prices went up twelve percent didn't they");

        var result = BleedFilter.Apply([system, mic], Kinds);

        Assert.Equal([system], result.Segments);
    }

    [Fact]
    public void DifferentWordsAtTheSameTimeAreTwoPeople()
    {
        var segments = new[] { Segment("system", 0, 3, "Let me share my screen with you now."), Segment("mic", 0.1, 3.1, "I think we should wait for the others.") };

        var result = BleedFilter.Apply(segments, Kinds);

        Assert.Same(segments, result.Segments);
        Assert.Empty(result.Dropped);
    }

    [Fact]
    public void TheSameWordsFarApartInTimeAreBothKept()
    {
        var segments = new[] { Segment("system", 0, 3, "Can everyone hear me now?"), Segment("mic", 20, 23, "Can everyone hear me now?") };

        var result = BleedFilter.Apply(segments, Kinds);

        Assert.Same(segments, result.Segments);
    }

    [Fact]
    public void AShortMatchIsNotBleed()
    {
        // Two people both saying "Yes, sure." within a second of each other.
        var segments = new[] { Segment("system", 0, 0.8, "Yes, sure."), Segment("mic", 0.1, 0.9, "Yes, sure.") };

        var result = BleedFilter.Apply(segments, Kinds);

        Assert.Same(segments, result.Segments);
    }

    [Fact]
    public void AShortLineLongEnoughInTimeCounts()
    {
        // Three words, but 2 s of them: long enough to be the speakers heard twice.
        var segments = new[] { Segment("system", 0, 2.2, "Absolutely not, Rowan."), Segment("mic", 0.2, 2.3, "Absolutely not, Rowan.") };

        var result = BleedFilter.Apply(segments, Kinds);

        Assert.Single(result.Segments);
    }

    [Fact]
    public void TheOverlapMustCoverMostOfTheShorterLine()
    {
        var system = Segment("system", 0, 4, "The quarterly numbers look better than expected.");
        // 2.2 s of a 4 s line overlap: 55 %, under the 60 % the filter asks for.
        var late = Segment("mic", 1.8, 5.8, "The quarterly numbers look better than expected.");
        Assert.Same(late, Assert.Single(BleedFilter.Apply([system, late], Kinds).Segments, s => s.Track == "mic"));

        // 2.4 s: 60 % exactly, enough.
        var onTime = Segment("mic", 1.6, 5.6, "The quarterly numbers look better than expected.");
        Assert.DoesNotContain(BleedFilter.Apply([system, onTime], Kinds).Segments, s => s.Track == "mic");
    }

    [Fact]
    public void AMicrophoneLineMatchedByTwoSystemLinesIsDroppedOnce()
    {
        var first = Segment("system", 0, 2, "We approve the marketing budget.");
        var second = Segment("system", 2, 4, "We approve the marketing budget.");
        var mic = Segment("mic", 0.1, 3.9, "We approve the marketing budget. We approve the marketing budget.");

        var result = BleedFilter.Apply([first, mic, second], Kinds);

        Assert.Equal([first, second], result.Segments);
        Assert.Single(result.Dropped);
    }

    [Fact]
    public void AnEditedLineIsNeverDropped()
    {
        var edited = Segment("mic", 0.1, 3, "So the budget for next year is approved.", new TranscriptEdit(DateTimeOffset.UnixEpoch, "So the budget"));
        var segments = new[] { Segment("system", 0, 3, "So the budget for next year is approved."), edited };

        var result = BleedFilter.Apply(segments, Kinds);

        Assert.Same(segments, result.Segments);
    }

    [Fact]
    public void ARecordingWithOneKindOfTrackChangesNothing()
    {
        var mics = new[] { Segment("mic", 0, 3, "So the budget for next year is approved."), Segment("mic2", 0.1, 3, "So the budget for next year is approved.") };
        Assert.Same(mics, BleedFilter.Apply(mics, Kinds).Segments);

        var systems = new[] { Segment("system", 0, 3, "So the budget for next year is approved."), Segment("teams", 0.1, 3, "So the budget for next year is approved.") };
        Assert.Same(systems, BleedFilter.Apply(systems, Kinds).Segments);

        // A mix, or an imported file: no kinds the filter knows.
        var mix = new[] { Segment("mix", 0, 3, "So the budget for next year is approved.") };
        Assert.Same(mix, BleedFilter.Apply(mix, Kinds).Segments);
    }

    [Fact]
    public void EveryMicrophoneCopyGoesAndTheApplicationTrackCountsAsClean()
    {
        var teams = Segment("teams", 0, 3, "So the budget for next year is approved.");
        var mic = Segment("mic", 0.1, 3.1, "So the budget for next year is approved.");
        var mic2 = Segment("mic2", 0.2, 3.2, "the budget for next year is approved");

        var result = BleedFilter.Apply([teams, mic, mic2], Kinds);

        Assert.Equal([teams], result.Segments);
        Assert.Equal([mic, mic2], result.Dropped.Select(d => d.Line));
    }

    [Fact]
    public void WordsAreLowerCaseWithoutAccentsOrPunctuation()
    {
        Assert.Equal(["cafe", "prices", "didn't", "they", "12"], BleedFilter.Words("  Café PRICES — didn't they?! 12. "));
        Assert.Empty(BleedFilter.Words(" … "));
    }
}
