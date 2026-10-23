using Memento.Core.Bridge.Contracts;
using Memento.Core.Transcripts;
using static Memento.Core.Tests.Fakes.TranscriptFixtures;

namespace Memento.Core.Tests.Transcripts;

/// <summary><see cref="SegmentJoiner"/>: a line joined with the next line of its track (2.6.0), the Undo of a split.</summary>
public sealed class SegmentJoinerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

    private static List<TranscriptSegment> Lines() =>
    [
        Segment("s0001", 0, 4, "Welcome everyone.", "spk1"),
        Segment("s0002", 4.5, 9, "Let's review the budget.", "spk1"),
        Segment("s0003", 9.5, 15, "The budget is approved.", "spk2"),
    ];

    [Fact]
    public void TheNextLineOfTheTrackJoinsTheFirstWhichKeepsItsIdAndSpeaker()
    {
        var lines = Lines();

        var result = SegmentJoiner.Join(lines, "s0001", Now);

        Assert.Null(result.Refusal);
        var joined = result.Joined!;
        Assert.Equal(("s0001", "Welcome everyone. Let's review the budget.", 0.0, 9.0, "spk1", 0.9), (joined.Id, joined.Text, joined.Start, joined.End, joined.Speaker, joined.SpeakerConfidence));
        Assert.Equal(lines[0].Words.Concat(lines[1].Words), joined.Words);
        Assert.Equal(0.8, joined.Confidence);
        Assert.Null(joined.Edited);
        Assert.Equal("s0002", result.RemovedId);
        Assert.Equal(["s0001", "s0003"], result.Segments!.Select(s => s.Id));
    }

    [Fact]
    public void SplittingThenJoiningGivesTheLineBack()
    {
        var lines = Lines();
        var cut = SegmentSplitter.Cut(lines[1], 6, lines.Select(l => l.Id).ToHashSet(StringComparer.Ordinal));
        var split = new List<TranscriptSegment> { lines[0], cut.First!, cut.Second!, lines[2] };

        var result = SegmentJoiner.Join(split, "s0002", Now);

        var joined = result.Joined!;
        Assert.Equal((lines[1].Text, lines[1].Start, lines[1].End, lines[1].Confidence), (joined.Text, joined.Start, joined.End, joined.Confidence));
        Assert.Equal(lines[1].Words, joined.Words);
        Assert.Null(joined.Edited);
        Assert.Equal(["s0001", "s0002", "s0003"], result.Segments!.Select(s => s.Id));
    }

    [Fact]
    public void TheLastLineAndALineFollowedByAnotherTrackAreRefused()
    {
        var lines = Lines();
        Assert.Equal("There is no line after this one.", SegmentJoiner.Join(lines, "s0003", Now).Refusal);

        lines[1] = lines[1] with { Track = "system" };
        Assert.Equal("The next line is on another track, so the two cannot be joined.", SegmentJoiner.Join(lines, "s0001", Now).Refusal);
        Assert.Null(SegmentJoiner.Join(lines, "s0001", Now).Segments);
    }

    [Fact]
    public void AnEditedLineMakesTheJoinedLineEditedWithTheOriginalsJoined()
    {
        var lines = Lines();
        lines[1] = lines[1] with { Edited = new TranscriptEdit(Now.AddHours(-1), "Lets review the budget") };

        var joined = SegmentJoiner.Join(lines, "s0001", Now).Joined!;

        Assert.Equal(new TranscriptEdit(Now, "Welcome everyone. Lets review the budget"), joined.Edited);
    }

    [Fact]
    public void TwoPartsOfOneEditedLineShareTheirOriginalOnce()
    {
        var lines = Lines();
        var edit = new TranscriptEdit(Now.AddHours(-1), "Lets review the budget");
        var cut = SegmentSplitter.Cut(lines[1] with { Edited = edit }, 6, lines.Select(l => l.Id).ToHashSet(StringComparer.Ordinal));
        var split = new List<TranscriptSegment> { lines[0], cut.First!, cut.Second!, lines[2] };

        var joined = SegmentJoiner.Join(split, "s0002", Now).Joined!;

        Assert.Equal(new TranscriptEdit(Now, "Lets review the budget"), joined.Edited);
    }

    [Fact]
    public void ALineWithoutWordsLeavesTheJoinedLineWithoutWords()
    {
        var lines = Lines();
        lines[1] = lines[1] with { Words = [] };

        Assert.Empty(SegmentJoiner.Join(lines, "s0001", Now).Joined!.Words);
    }
}
