using Memento.Core.Bridge.Contracts;
using Memento.Core.Transcripts;
using static Memento.Core.Tests.Fakes.TranscriptFixtures;

namespace Memento.Core.Tests.Transcripts;

/// <summary><see cref="SpeakerSplitter"/> (2.6.0): a speaker's raw voices grouped into two by sound, the smaller half a new speaker.</summary>
public sealed class SpeakerSplitterTests
{
    private static readonly float[] Right = [1f, 0f];
    private static readonly float[] Up = [0f, 1f];
    private static readonly float[] NearRight = [0.95f, 0.31f];
    private static readonly float[] AlikeRight = [0.98f, 0.2f];

    private static VoicesDocument Voices(params VoiceCluster[] clusters) => new(VoicesDocument.CurrentSchemaVersion, "nemo-titanet-small", "sig", [], clusters);

    private static VoiceCluster Cluster(int number, float[] embedding, params string[] lines) => new("mic", number, embedding, 10, lines);

    /// <summary>Speaker 1 says s1 (4 s), s3 (5.5 s) and s4 (1.5 s); Speaker 2 says s2.</summary>
    private static TranscriptDocument Meeting() => Document(
        Segment("s1", 0, 4, "Welcome everyone to the planning meeting.", "spk1"),
        Segment("s2", 4.5, 9, "Thanks. Let's review the budget first.", "spk2"),
        Segment("s3", 9.5, 15, "The budget for the third quarter is approved.", "spk1"),
        Segment("s4", 15.5, 17, "Good, thanks.", "spk1"));

    [Fact]
    public void TwoDifferentVoicesSeparateAndTheBiggerHalfKeepsTheSpeaker()
    {
        var doc = Meeting();
        var voices = Voices(Cluster(0, Right, "s1"), Cluster(1, Up, "s3", "s4"));

        var result = SpeakerSplitter.Split(doc.Segments, doc.Speakers, voices, "spk1");

        Assert.Null(result.Refusal);
        Assert.Equal(["s1"], result.SegmentIds);
        var created = Assert.IsType<Speaker>(result.Created);
        Assert.Equal(("spk3", "Speaker 3", false, 3), (created.Id, created.Name, created.Renamed, created.Color));
        Assert.Equal(["spk1", "spk2", "spk3"], result.Speakers.Select(s => s.Id));
        Assert.Equal([7_000L, 4_500L, 4_000L], result.Speakers.Select(s => s.TalkTimeMs));
        Assert.Equal(["spk3", "spk2", "spk1", "spk1"], result.Segments.Select(s => s.Speaker));
        // The moved line keeps its confidence and is not the user's.
        Assert.Equal(0.9, result.Segments[0].SpeakerConfidence);
        Assert.False(result.Segments[0].SpeakerByUser);
    }

    [Fact]
    public void ALineTheUserPlacedStaysEvenWhenItsVoiceLeaves()
    {
        var doc = Meeting();
        // s3 is stretched to 10.5 s so the voice holding s1 and the placed s4 (5.5 s) is the half that leaves.
        var segments = doc.Segments
            .Select(s => s.Id == "s4" ? s with { SpeakerByUser = true } : s.Id == "s3" ? s with { End = 20 } : s)
            .ToList();
        var voices = Voices(Cluster(0, Right, "s1", "s4"), Cluster(1, Up, "s3"));

        var result = SpeakerSplitter.Split(segments, doc.Speakers, voices, "spk1");

        Assert.Null(result.Refusal);
        Assert.Equal(["s1"], result.SegmentIds);
        Assert.Equal("spk1", result.Segments.Single(s => s.Id == "s4").Speaker);
        Assert.True(result.Segments.Single(s => s.Id == "s4").SpeakerByUser);
    }

    [Fact]
    public void ALineInNoVoiceStays()
    {
        var doc = Meeting();
        // s4 is in no cluster: it has no voice to be grouped by, so it stays with Speaker 1.
        var voices = Voices(Cluster(0, Right, "s1"), Cluster(1, Up, "s3"));

        var result = SpeakerSplitter.Split(doc.Segments, doc.Speakers, voices, "spk1");

        Assert.Null(result.Refusal);
        // s3 (5.5 s) outweighs s1 (4 s): s1 leaves.
        Assert.Equal(["s1"], result.SegmentIds);
        Assert.Equal("spk1", result.Segments.Single(s => s.Id == "s4").Speaker);
    }

    [Fact]
    public void AlikeVoicesGroupTogetherAgainstTheDifferentOne()
    {
        var doc = Document(
            Segment("s1", 0, 4, "One.", "spk1"),
            Segment("s2", 4.5, 10, "Two.", "spk1"),
            Segment("s3", 10.5, 13, "Three.", "spk1"));
        var voices = Voices(Cluster(0, Right, "s1"), Cluster(1, NearRight, "s2"), Cluster(2, Up, "s3"));

        var result = SpeakerSplitter.Split(doc.Segments, doc.Speakers, voices, "spk1");

        Assert.Null(result.Refusal);
        Assert.Equal(["s3"], result.SegmentIds);
        Assert.Equal(["spk1", "spk1", "spk2"], result.Segments.Select(s => s.Speaker));
    }

    [Fact]
    public void OneVoiceCannotBeSplit()
    {
        var doc = Meeting();
        var voices = Voices(Cluster(0, Right, "s1", "s3", "s4"), Cluster(1, Up, "s2"));

        var result = SpeakerSplitter.Split(doc.Segments, doc.Speakers, voices, "spk1");

        Assert.Equal("Speaker 1 was heard as one voice, so there is nothing to split by sound. Move lines with their menu instead.", result.Refusal);
        Assert.Null(result.Created);
        Assert.Empty(result.SegmentIds);
        Assert.Same(doc.Segments, result.Segments);
        Assert.Same(doc.Speakers, result.Speakers);
    }

    [Fact]
    public void VoicesThatSoundAlikeAreNotSplit()
    {
        var doc = Meeting();
        var voices = Voices(Cluster(0, Right, "s1"), Cluster(1, AlikeRight, "s3", "s4"));

        var result = SpeakerSplitter.Split(doc.Segments, doc.Speakers, voices, "spk1");

        Assert.Equal("Speaker 1's voices sound alike (0.98), so Memento cannot split them by sound.", result.Refusal);
        Assert.Null(result.Created);
    }

    [Fact]
    public void WithoutVoicesNothingCanBeSplit()
    {
        var doc = Meeting();

        var result = SpeakerSplitter.Split(doc.Segments, doc.Speakers, null, "spk1");

        Assert.StartsWith("Speaker 1 was heard as one voice", result.Refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOtherHalfOfOnlyPlacedLinesLeavesNothingToMove()
    {
        var doc = Meeting();
        var segments = doc.Segments.Select(s => s.Id == "s1" ? s with { SpeakerByUser = true } : s).ToList();
        var voices = Voices(Cluster(0, Right, "s1"), Cluster(1, Up, "s3", "s4"));

        var result = SpeakerSplitter.Split(segments, doc.Speakers, voices, "spk1");

        Assert.Equal("Speaker 1's other voice holds only lines you placed there yourself, so nothing can move.", result.Refusal);
    }

    [Fact]
    public void TheGuardsAreTheDocumentedOnes()
    {
        Assert.Equal(0.8, SpeakerSplitter.AlikeSimilarity);
        Assert.Equal(10, SpeakerSplitter.MaxRounds);
    }
}
