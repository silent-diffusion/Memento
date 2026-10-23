using Memento.Core.Bridge.Contracts;
using Memento.Core.Transcripts;

namespace Memento.Core.Tests.Transcripts;

/// <summary><see cref="SegmentSplitter"/>: one line cut in two at a word (2.6.0).</summary>
public sealed class SegmentSplitterTests
{
    private static readonly TranscriptEdit Edit = new(new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero), "Welcome every one to the meeting.");

    /// <summary>"Welcome everyone to the meeting." over 0–4 s, with word times and confidences of its own.</summary>
    private static TranscriptSegment Line(TranscriptEdit? edited = null) => new(
        "s0007",
        0,
        4,
        "mic",
        "spk2",
        0.85,
        "Welcome everyone to the meeting.",
        0.6,
        [new("Welcome", 0, 0.8, 0.9), new("everyone", 0.9, 1.7, 0.6), new("to", 1.8, 2.0, 0.95), new("the", 2.0, 2.3, 0.7), new("meeting.", 2.4, 3.9, 0.8)],
        edited)
    { SpeakerByUser = true };

    private static HashSet<string> Taken(params string[] ids) => ids.ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void TheCutSnapsToTheNearestWordBoundaryAndTheWordsFollowTheirParts()
    {
        var result = SegmentSplitter.Cut(Line(), 10, Taken("s0007"));

        Assert.Null(result.Refusal);
        var first = result.First!;
        var second = result.Second!;
        Assert.Equal(("s0007", "Welcome", 0.0, 0.8), (first.Id, first.Text, first.Start, first.End));
        Assert.Equal(("s0007-2", "everyone to the meeting.", 0.9, 4.0), (second.Id, second.Text, second.Start, second.End));
        Assert.Equal(["Welcome"], first.Words.Select(w => w.W));
        Assert.Equal(["everyone", "to", "the", "meeting."], second.Words.Select(w => w.W));
        Assert.Equal((0.9, 0.6), (first.Confidence, second.Confidence));
    }

    [Fact]
    public void ACutInsideAWordGoesToTheCloserSide()
    {
        // "Welcome everyone to| the" (offset 20): "to" starts at 17, "the" at 20 → before "the".
        var atThe = SegmentSplitter.Cut(Line(), 19, Taken("s0007"));
        Assert.Equal(("Welcome everyone to", "the meeting."), (atThe.First!.Text, atThe.Second!.Text));

        // "Welcome ever|yone" (offset 12): "everyone" starts at 8, "to" at 17 → before "everyone".
        var atEveryone = SegmentSplitter.Cut(Line(), 12, Taken("s0007"));
        Assert.Equal(("Welcome", "everyone to the meeting."), (atEveryone.First!.Text, atEveryone.Second!.Text));
    }

    [Fact]
    public void SpeakerFieldsAndTheEditGoToBothParts()
    {
        var result = SegmentSplitter.Cut(Line(Edit), 17, Taken("s0007"));

        foreach (var part in new[] { result.First!, result.Second! })
        {
            Assert.Equal(("spk2", 0.85, true, "mic"), (part.Speaker, part.SpeakerConfidence, part.SpeakerByUser, part.Track));
            Assert.Equal(Edit, part.Edited);
        }
    }

    [Fact]
    public void TheSecondPartsIdSkipsIdsTheTranscriptHas()
    {
        var taken = Taken("s0007", "s0007-2", "s0007-3");

        var result = SegmentSplitter.Cut(Line(), 10, taken);

        Assert.Equal("s0007-4", result.Second!.Id);
        Assert.Contains("s0007-4", taken);
    }

    [Fact]
    public void WithoutUsableWordsTheTimesFollowTheCharacters()
    {
        // Words of another wording do not describe this text, so they are not used either.
        var stale = Line() with { Words = [new("Hello", 0, 1, 1), new("there", 1, 2, 1)] };
        foreach (var line in new[] { Line() with { Words = [] }, stale })
        {
            var result = SegmentSplitter.Cut(line, 17, Taken("s0007"));

            // "Welcome everyone " is 17 of 32 characters: the cut falls at 4 × 17 / 32 = 2.125 s.
            Assert.Equal((2.125, 2.125), (result.First!.End, result.Second!.Start));
            Assert.Empty(result.First.Words);
            Assert.Empty(result.Second.Words);
            Assert.Equal((0.6, 0.6), (result.First.Confidence, result.Second.Confidence));
        }
    }

    [Fact]
    public void ACutOutsideTheWordsOrALineOfOneWordIsRefused()
    {
        Assert.Equal("Choose a place inside the line's words.", SegmentSplitter.Cut(Line(), 0, Taken("s0007")).Refusal);
        Assert.Equal("Choose a place inside the line's words.", SegmentSplitter.Cut(Line(), 32, Taken("s0007")).Refusal);
        var oneWord = Line() with { Text = "Welcome.", Words = [] };
        Assert.Equal("Both parts of a split line need at least one word.", SegmentSplitter.Cut(oneWord, 3, Taken("s0007")).Refusal);
        Assert.Null(SegmentSplitter.Cut(oneWord, 3, Taken("s0007")).First);
    }
}
