using Memento.Core.Transcripts;
using static Memento.Core.Tests.Fakes.TranscriptFixtures;

namespace Memento.Core.Tests.Transcripts;

/// <summary><see cref="SameVoice"/>: the other lines of the raw voices behind the asked lines, from <c>voices.json</c>.</summary>
public sealed class SameVoiceTests
{
    private static readonly string[] S1 = ["s1"];
    private static readonly string[] S1AndS2 = ["s1", "s2"];
    private static readonly string[] S4 = ["s4"];

    /// <summary>Voice A (mic 0) won s1, s3 and s5; voice B (mic 1) won s2; s4 was won by no voice.</summary>
    private static VoicesDocument Voices() => new(
        VoicesDocument.CurrentSchemaVersion,
        "nemo-titanet-small",
        "sig",
        [],
        [
            new VoiceCluster("mic", 0, [1f, 0f], 14, ["s5", "s1", "s3", "s9"]),
            new VoiceCluster("mic", 1, [0f, 1f], 8, ["s2"]),
        ]);

    private static TranscriptDocument Transcript() => Document(
        Segment("s1", 0, 4, "One.", "spk1"),
        Segment("s2", 4, 8, "Two.", "spk2"),
        Segment("s3", 8, 12, "Three.", "spk1"),
        Segment("s4", 12, 16, "Four.", "spk1"),
        Segment("s5", 16, 20, "Five.", "spk3"));

    [Fact]
    public void TheOtherLinesOfTheVoiceComeBackInTranscriptOrderWithoutTheAskedOne()
    {
        Assert.Equal(["s3", "s5"], SameVoice.Lines(Transcript().Segments, Voices(), S1));
    }

    [Fact]
    public void SeveralAskedLinesGiveTheUnionOfTheirVoices()
    {
        Assert.Equal(["s3", "s5"], SameVoice.Lines(Transcript().Segments, Voices(), S1AndS2));
    }

    [Fact]
    public void ALineNoVoiceHoldsHasNone()
    {
        Assert.Empty(SameVoice.Lines(Transcript().Segments, Voices(), S4));
    }

    [Fact]
    public void LinesTheTranscriptNoLongerHasAreLeftOut()
    {
        // s9 is in the voice but not in the transcript any more.
        Assert.DoesNotContain("s9", SameVoice.Lines(Transcript().Segments, Voices(), S1));
    }

    [Fact]
    public void WithoutVoicesThereIsNothing()
    {
        Assert.Empty(SameVoice.Lines(Transcript().Segments, null, S1));
    }
}
