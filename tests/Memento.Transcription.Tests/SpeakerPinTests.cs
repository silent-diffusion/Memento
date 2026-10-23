using Memento.Core.Bridge.Contracts;
using Memento.Core.Workers;
using Memento.Transcription.Speakers;

namespace Memento.Transcription.Tests;

/// <summary>
/// Lines the user placed guide the grouping (2.6.0): <see cref="SpeakerAssigner"/> with <see cref="SpeakerPin"/>s, and
/// <see cref="SpeakerNamer"/> with the names they pin.
/// </summary>
public sealed class SpeakerPinTests
{
    private static TranscriptSegment Segment(string id, double start, double end, string track, bool byUser = false) =>
        new(id, start, end, track, null, null, "text", 0.9, [], null) { SpeakerByUser = byUser };

    /// <summary>
    /// One track: readers A and B (0.6 alike) talk for 40 and 30 seconds; the diarizer also made a cluster of a 3-second
    /// laugh that sounds like nobody, and split 12 seconds of A off as a cluster of its own (SpeakerAssignerTests).
    /// </summary>
    private static (TranscriptSegment[] Segments, DiarizedTrack[] Tracks) SplitMeeting()
    {
        var segments = new[]
        {
            Segment("a1", 0, 20, "mic"), Segment("b1", 20, 35, "mic"), Segment("l1", 35, 38, "mic"),
            Segment("a2", 38, 58, "mic"), Segment("b2", 58, 73, "mic"), Segment("a3", 73, 85, "mic"),
        };
        var turns = new[]
        {
            new SpeakerTurn(0, 20, 0, 0.7), new SpeakerTurn(20, 35, 1, 0.7), new SpeakerTurn(35, 38, 2, 0.3),
            new SpeakerTurn(38, 58, 0, 0.7), new SpeakerTurn(58, 73, 1, 0.7), new SpeakerTurn(73, 85, 3, 0.6),
        };
        SpeakerVoice[] voices =
        [
            new(0, [1f, 0f, 0f], 40), new(1, [0.6f, 0.8f, 0f], 30),
            new(2, [0.1f, 0f, 1f], 3), new(3, [0.95f, 0.3f, 0f], 12),
        ];
        return (segments, [new DiarizedTrack("mic", turns, voices, 85)]);
    }

    private static string? SpeakerOf(SpeakerAssigner.Result result, string id) => result.Segments.Single(s => s.Id == id).Speaker;

    [Fact]
    public void APlacedLineKeepsItsPersonAndPinsItsVoice()
    {
        var (segments, tracks) = SplitMeeting();

        // The user put a3 (A's split-off voice) on Rowan; A's main voice joins it by sound and takes the label along.
        var result = SpeakerAssigner.Assign(segments, tracks, expectedSpeakers: 2, minSpeechSeconds: 10, pins: [new SpeakerPin("a3", "Rowan Hale", true)]);

        Assert.Equal(["spk1", "spk2", "spk1", "spk1", "spk2", "spk1"], result.Segments.Select(s => s.Speaker));
        Assert.Equal(1, result.Pinned);
        Assert.Equal(new Dictionary<string, string> { ["spk1"] = "Rowan Hale" }, result.Names);
        var placed = result.Segments.Single(s => s.Id == "a3");
        Assert.True(placed.SpeakerByUser);
        Assert.Equal(1.0, placed.SpeakerConfidence);
        Assert.All(result.Segments.Where(s => s.Id != "a3"), s => Assert.False(s.SpeakerByUser));
    }

    [Fact]
    public void VoicesPinnedToDifferentPeopleAreNeverJoinedSoACountBelowThemIsNotReached()
    {
        var (segments, tracks) = SplitMeeting();

        var result = SpeakerAssigner.Assign(
            segments,
            tracks,
            expectedSpeakers: 1,
            minSpeechSeconds: 10,
            pins: [new SpeakerPin("a1", "Ana Lind", true), new SpeakerPin("a3", "Ben Ortega", true)]);

        // Without pins one count joins everyone; Ana's voice and Ben's stay apart, and B (unpinned) joins the one it is most like.
        Assert.Single(SpeakerAssigner.Assign(segments, tracks, expectedSpeakers: 1, minSpeechSeconds: 10).Speakers);
        Assert.Equal(2, result.Speakers.Count);
        Assert.Equal(2, result.Pinned);
        Assert.Equal(SpeakerOf(result, "a1"), SpeakerOf(result, "a2"));
        Assert.NotEqual(SpeakerOf(result, "a1"), SpeakerOf(result, "a3"));
        Assert.Equal(new Dictionary<string, string> { ["spk1"] = "Ana Lind", ["spk2"] = "Ben Ortega" }, result.Names);
    }

    [Fact]
    public void VoicesPinnedToTheSamePersonJoinFirstWhateverTheySoundLike()
    {
        var (segments, tracks) = SplitMeeting();

        // Auto with a threshold nothing reaches: A and B (0.6 alike) are still one person because the user said so.
        var result = SpeakerAssigner.Assign(
            segments,
            tracks,
            null,
            joinSimilarity: 0.999,
            minSpeechSeconds: 10,
            foldSimilarity: 0.95,
            pins: [new SpeakerPin("a1", "Ana Lind", true), new SpeakerPin("b1", "Ana Lind", true)]);

        Assert.Equal(["spk1", "spk1", "spk2", "spk1", "spk1", "spk3"], result.Segments.Select(s => s.Speaker));
        Assert.Equal(1, result.Merged);
        Assert.Equal(2, result.Pinned);
        Assert.Equal(new Dictionary<string, string> { ["spk1"] = "Ana Lind" }, result.Names);
    }

    [Fact]
    public void AnUnpinnedLittleVoiceStillFoldsIntoAMainVoice()
    {
        var (segments, tracks) = SplitMeeting();

        var result = SpeakerAssigner.Assign(segments, tracks, expectedSpeakers: 2, minSpeechSeconds: 10, pins: [new SpeakerPin("a1", "Ana Lind", true)]);

        Assert.Equal(2, result.Speakers.Count);
        Assert.Contains(SpeakerOf(result, "l1"), result.Speakers.Select(s => s.Id));
    }

    [Fact]
    public void APlacedLineWhoseVoiceWentToAnotherPersonKeepsItsOwn()
    {
        var (segments, tracks) = SplitMeeting();

        // a1 and a2 share one voice; the user put them on different people, so the voice goes to the first label and Ben
        // keeps a2 as a person of his own, after the others.
        var result = SpeakerAssigner.Assign(
            segments,
            tracks,
            expectedSpeakers: 2,
            minSpeechSeconds: 10,
            pins: [new SpeakerPin("a1", "Ana Lind", true), new SpeakerPin("a2", "Ben Ortega", true)]);

        Assert.Equal(3, result.Speakers.Count);
        Assert.Equal("spk1", SpeakerOf(result, "a1"));
        Assert.Equal("spk3", SpeakerOf(result, "a2"));
        Assert.Equal("spk1", SpeakerOf(result, "a3"));
        var moved = result.Segments.Single(s => s.Id == "a2");
        Assert.True(moved.SpeakerByUser);
        Assert.Equal(1.0, moved.SpeakerConfidence);
        Assert.Equal(new Dictionary<string, string> { ["spk1"] = "Ana Lind", ["spk3"] = "Ben Ortega" }, result.Names);
        Assert.Equal(20_000L, result.Speakers[2].TalkTimeMs);
    }

    [Fact]
    public void ADefaultNamePinsTheVoiceButIsNotCarried()
    {
        var (segments, tracks) = SplitMeeting();

        var result = SpeakerAssigner.Assign(
            segments,
            tracks,
            expectedSpeakers: 1,
            minSpeechSeconds: 10,
            pins: [new SpeakerPin("a1", "Speaker 1", false), new SpeakerPin("b1", "Speaker 2", false)]);

        Assert.Equal(2, result.Speakers.Count);
        Assert.NotEqual(SpeakerOf(result, "a1"), SpeakerOf(result, "b1"));
        Assert.Empty(result.Names!);
    }

    [Fact]
    public void AFlagOnAnUnpinnedLineIsCleared()
    {
        var (segments, tracks) = SplitMeeting();
        segments[1] = Segment("b1", 20, 35, "mic", byUser: true);

        var result = SpeakerAssigner.Assign(segments, tracks, expectedSpeakers: 2, minSpeechSeconds: 10);

        Assert.All(result.Segments, s => Assert.False(s.SpeakerByUser));
        Assert.Equal(0, result.Pinned);
        Assert.Empty(result.Names!);
    }

    private static TranscriptSegment Line(string id, double start, double end, string speaker) =>
        new(id, start, end, "mic", speaker, 0.9, "text", 0.9, [], null);

    private static Speaker Unnamed(int n) => new($"spk{n}", $"Speaker {n}", false, ((n - 1) % 4) + 1, 0);

    [Fact]
    public void APinnedNameBeatsTheSharedLinesCarryOverAndIsGivenOnce()
    {
        // The new pass: spk1 speaks first (0–10, 20–30), then spk2 (10–20), then spk3 (30–35).
        Speaker[] speakers = [Unnamed(1), Unnamed(2), Unnamed(3)];
        TranscriptSegment[] segments = [Line("s1", 0, 10, "spk1"), Line("s2", 10, 20, "spk2"), Line("s3", 20, 30, "spk1"), Line("s4", 30, 35, "spk3")];
        // Before, Sam had s1 and s3 (which the new spk1 shares), but the user put s2 on Sam: that pin names spk2.
        Speaker[] before = [new("spk7", "Sam Okafor", true, 3, 0)];
        TranscriptSegment[] old = [Line("s1", 0, 10, "spk7"), Line("s2", 10, 20, "spk7"), Line("s3", 20, 30, "spk7")];

        var result = SpeakerNamer.Name(speakers, segments, before, old, ["Sam Okafor", "Avery Stone"], new Dictionary<string, string> { ["spk2"] = "Sam Okafor" });

        Assert.Equal(["Avery Stone", "Sam Okafor", "Speaker 3"], result.Speakers.Select(s => s.Name));
        Assert.Equal([true, true, false], result.Speakers.Select(s => s.Renamed));
        Assert.Equal((1, 1), (result.Carried, result.Known));
    }
}
