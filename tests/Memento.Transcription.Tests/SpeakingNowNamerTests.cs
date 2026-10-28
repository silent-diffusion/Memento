using Memento.Core.Bridge.Contracts;
using Memento.Core.Transcripts;
using Memento.Core.Workers;
using Memento.Transcription.Speakers;

namespace Memento.Transcription.Tests;

/// <summary>Speaking now marks name the person whose voice was heard at the pressed moment (2.6.0, <see cref="SpeakingNowNamer"/>).</summary>
public sealed class SpeakingNowNamerTests
{
    private static readonly string[] Known = ["Ana", "Ben"];

    private static TranscriptSegment Line(string id, double start, double end, string speaker, string track = "mic") =>
        new(id, start, end, track, speaker, 0.9, "text", 0.9, [], null);

    /// <summary>
    /// One track, two raw voices: cluster 0 talks 0–10 and 20–30 (lines s1, s3 → spk1), cluster 1 talks 10–20 (s2 → spk2).
    /// </summary>
    private static (DiarizedTrack[] Tracks, VoiceCluster[] Voices, TranscriptSegment[] Segments) Meeting() =>
        ([new DiarizedTrack("mic", [new SpeakerTurn(0, 10, 0, 0.7), new SpeakerTurn(10, 20, 1, 0.7), new SpeakerTurn(20, 30, 0, 0.7)])],
        [new VoiceCluster("mic", 0, [1f, 0f], 20, ["s1", "s3"]), new VoiceCluster("mic", 1, [0f, 1f], 10, ["s2"])],
        [Line("s1", 0, 10, "spk1"), Line("s2", 10, 20, "spk2"), Line("s3", 20, 30, "spk1")]);

    private static IReadOnlyDictionary<string, string> Names(params SpokeMark[] marks)
    {
        var (tracks, voices, segments) = Meeting();
        return SpeakingNowNamer.Names(marks, Known, tracks, voices, segments);
    }

    [Fact]
    public void AMarkNamesThePersonWhoseVoiceWasHeardThen()
    {
        var names = Names(new SpokeMark("ana", 15_000));

        Assert.Equal(new Dictionary<string, string> { ["spk2"] = "Ana" }, names);
    }

    [Fact]
    public void TheNameGoesToThePersonWithTheMostMarks()
    {
        var names = Names(new SpokeMark("Ana", 5_000), new SpokeMark("Ana", 25_000), new SpokeMark("Ana", 15_000));

        Assert.Equal(new Dictionary<string, string> { ["spk1"] = "Ana" }, names);
    }

    [Fact]
    public void ATieGoesToThePersonWhoSpeaksFirst()
    {
        var names = Names(new SpokeMark("Ana", 15_000), new SpokeMark("Ana", 25_000));

        Assert.Equal(new Dictionary<string, string> { ["spk1"] = "Ana" }, names);
    }

    [Fact]
    public void OneNamePerPersonAndOnePersonPerName()
    {
        // Both names were pressed while spk1 talked; the first as listed wins and Ben is left for the order of appearance.
        var names = Names(new SpokeMark("Ben", 25_000), new SpokeMark("Ana", 5_000));

        Assert.Equal(new Dictionary<string, string> { ["spk1"] = "Ana" }, names);
    }

    [Fact]
    public void AMarkJustAfterATurnEndedStillMeansThatVoiceButLaterOnesSayNothing()
    {
        Assert.Equal(new Dictionary<string, string> { ["spk1"] = "Ana" }, Names(new SpokeMark("Ana", 30_500)));
        Assert.Empty(Names(new SpokeMark("Ana", 31_000)));
        Assert.Empty(Names(new SpokeMark("Ana", 45_000)));
    }

    [Fact]
    public void AMarkForANameNoLongerListedOrOnAVoiceWithoutLinesSaysNothing()
    {
        var (tracks, _, segments) = Meeting();

        Assert.Empty(Names(new SpokeMark("Chris", 15_000)));
        // No voices known (voices.json without clusters): nothing to vote on.
        Assert.Empty(SpeakingNowNamer.Names([new SpokeMark("Ana", 15_000)], Known, tracks, [], segments));
        Assert.Empty(SpeakingNowNamer.Names([], Known, tracks, [], segments));
    }

    [Fact]
    public void AVoiceVotesForThePersonMostOfItsLinesCarryNow()
    {
        // The user moved s3 to spk2 after the pass: cluster 0's lines are split 1–1, the first id wins the tie.
        var (tracks, voices, _) = Meeting();
        TranscriptSegment[] moved = [Line("s1", 0, 10, "spk1"), Line("s2", 10, 20, "spk2"), Line("s3", 20, 30, "spk2")];

        var names = SpeakingNowNamer.Names([new SpokeMark("Ana", 25_000)], Known, tracks, voices, moved);

        Assert.Equal(new Dictionary<string, string> { ["spk1"] = "Ana" }, names);
    }

    /// <summary>spk1 talks 0–10 (cluster 0), spk2 talks 10–30 (cluster 1).</summary>
    private static (DiarizedTrack[] Tracks, VoiceCluster[] Voices, TranscriptSegment[] Segments) LateComer() =>
        ([new DiarizedTrack("mic", [new SpeakerTurn(0, 10, 0, 0.7), new SpeakerTurn(10, 20, 1, 0.7), new SpeakerTurn(20, 30, 1, 0.7)])],
        [new VoiceCluster("mic", 0, [1f, 0f], 10, ["s1"]), new VoiceCluster("mic", 1, [0f, 1f], 20, ["s2", "s3"])],
        [Line("s1", 0, 10, "spk1"), Line("s2", 10, 20, "spk2"), Line("s3", 20, 30, "spk2")]);

    private static readonly DateTimeOffset Wall = new(2026, 1, 5, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void WithoutAnAddedTimeATieStillGoesToWhoSpeaksFirst()
    {
        var (tracks, voices, segments) = LateComer();

        var names = SpeakingNowNamer.Names([new SpokeMark("Ana", 5_000), new SpokeMark("Ana", 15_000)], Known, tracks, voices, segments);

        Assert.Equal(new Dictionary<string, string> { ["spk1"] = "Ana" }, names);
    }

    [Fact]
    public void TheAddedTimeBreaksATieTowardsTheVoiceHeardAfterIt()
    {
        var (tracks, voices, segments) = LateComer();

        // Ana was added at 0:12; spk1 is not heard after 0:10, so the tie goes to spk2.
        var names = SpeakingNowNamer.Names(
            [new SpokeMark("Ana", 5_000), new SpokeMark("Ana", 15_000)], Known, tracks, voices, segments, [new SpeakerAdded("ana", 12_000, Wall)]);

        Assert.Equal(new Dictionary<string, string> { ["spk2"] = "Ana" }, names);
    }

    [Fact]
    public void TheAddedTimeDoesNotOutweighMoreMarks()
    {
        var (tracks, voices, segments) = LateComer();

        var names = SpeakingNowNamer.Names(
            [new SpokeMark("Ana", 5_000), new SpokeMark("Ana", 6_000), new SpokeMark("Ana", 15_000)], Known, tracks, voices, segments, [new SpeakerAdded("Ana", 12_000, Wall)]);

        Assert.Equal(new Dictionary<string, string> { ["spk1"] = "Ana" }, names);
    }

    [Fact]
    public void AMarkThatHeardAnotherVoiceIsALowConfidenceName()
    {
        var (tracks, voices, segments) = LateComer();

        var doubts = SpeakingNowNamer.Doubts(
            [new SpokeMark("Ana", 5_000), new SpokeMark("Ana", 15_000)], [], tracks, voices, segments, new Dictionary<string, string> { ["spk2"] = "Ana" });

        var doubt = Assert.Single(doubts);
        Assert.Equal(new SpeakingNowNamer.Doubt("Ana", 5_000, "Speaking now heard a different voice"), doubt);
    }

    [Fact]
    public void ANameThatWentToSomeoneHeardOnlyBeforeItWasAddedIsALowConfidenceName()
    {
        var (tracks, voices, segments) = LateComer();
        var added = new[] { new SpeakerAdded("Ana", 12_000, Wall), new SpeakerAdded("Ben", 12_000, Wall) };

        var doubts = SpeakingNowNamer.Doubts([], added, tracks, voices, segments, new Dictionary<string, string> { ["spk1"] = "Ana", ["spk2"] = "Ben" });

        Assert.Equal([new SpeakingNowNamer.Doubt("Ana", 12_000, "heard only before the name was added")], doubts);
    }

    [Fact]
    public void AgreeingEvidenceGivesNoDoubts()
    {
        var (tracks, voices, segments) = LateComer();

        var doubts = SpeakingNowNamer.Doubts(
            [new SpokeMark("Ana", 15_000)], [new SpeakerAdded("Ana", 12_000, Wall)], tracks, voices, segments, new Dictionary<string, string> { ["spk2"] = "Ana" });

        Assert.Empty(doubts);
    }
}
