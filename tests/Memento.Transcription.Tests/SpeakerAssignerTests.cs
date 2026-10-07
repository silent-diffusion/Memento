using Memento.Core.Bridge.Contracts;
using Memento.Core.Workers;
using Memento.Transcription.Speakers;

namespace Memento.Transcription.Tests;

public sealed class SpeakerAssignerTests
{
    private static TranscriptSegment Segment(string id, double start, double end, string track) =>
        new(id, start, end, track, null, null, "text", 0.9, [], null);

    [Fact]
    public void EachSegmentGetsTheSpeakerThatOverlapsItMost()
    {
        var segments = new[] { Segment("s1", 0, 10, "mic"), Segment("s2", 10, 20, "mic"), Segment("s3", 20, 30, "mic") };
        var turns = new DiarizedTrack("mic", [new SpeakerTurn(0, 12, 0, 0.7), new SpeakerTurn(12, 21, 1, 0.65), new SpeakerTurn(21, 30, 0, 0.75)]);

        var result = SpeakerAssigner.Assign(segments, [turns]);

        Assert.Equal(["spk1", "spk2", "spk1"], result.Segments.Select(s => s.Speaker));
        Assert.Equal(["Speaker 1", "Speaker 2"], result.Speakers.Select(s => s.Name));
        Assert.Equal([1, 2], result.Speakers.Select(s => s.Color));
        Assert.Equal([20_000L, 10_000L], result.Speakers.Select(s => s.TalkTimeMs));
        Assert.All(result.Speakers, s => Assert.False(s.Renamed));
    }

    [Fact]
    public void SpeakersOfDifferentTracksAreDifferentPeople()
    {
        var segments = new[] { Segment("s1", 0, 5, "mic"), Segment("s2", 5, 10, "app-zoom"), Segment("s3", 10, 15, "mic") };
        var tracks = new[]
        {
            new DiarizedTrack("mic", [new SpeakerTurn(0, 15, 0, 0.9)]),
            new DiarizedTrack("app-zoom", [new SpeakerTurn(0, 15, 0, 0.9)]),
        };

        var result = SpeakerAssigner.Assign(segments, tracks);

        Assert.Equal(["spk1", "spk2", "spk1"], result.Segments.Select(s => s.Speaker));
        Assert.Equal(2, result.Speakers.Count);
    }

    [Fact]
    public void ColoursCycleThroughFourByFirstAppearance()
    {
        var segments = Enumerable.Range(0, 6).Select(i => Segment($"s{i}", i * 10, (i * 10) + 9, "mic")).ToArray();
        var turns = new DiarizedTrack("mic", Enumerable.Range(0, 6).Select(i => new SpeakerTurn(i * 10, (i * 10) + 9, 5 - i, 0.8)).ToList());

        var result = SpeakerAssigner.Assign(segments, [turns]);

        Assert.Equal(["spk1", "spk2", "spk3", "spk4", "spk5", "spk6"], result.Segments.Select(s => s.Speaker));
        Assert.Equal([1, 2, 3, 4, 1, 2], result.Speakers.Select(s => s.Color));
    }

    [Fact]
    public void ConfidenceIsCalibratedAndSharedWhenVoicesOverlap()
    {
        var segments = new[] { Segment("s1", 0, 10, "mic"), Segment("s2", 10, 20, "mic") };
        var turns = new DiarizedTrack("mic", [new SpeakerTurn(0, 10, 0, 0.6), new SpeakerTurn(10, 17, 1, 0.6), new SpeakerTurn(17, 20, 0, 0.6)]);

        var result = SpeakerAssigner.Assign(segments, [turns]);

        Assert.Equal(1.0, result.Segments[0].SpeakerConfidence);
        Assert.Equal(0.7, result.Segments[1].SpeakerConfidence);
    }

    [Theory]
    [InlineData(-2.0, 1.0)]
    [InlineData(0.2, 0.0)]
    [InlineData(0.4, 0.5)]
    [InlineData(0.75, 1.0)]
    [InlineData(-0.1, 0.0)]
    public void SherpaScoresAreCalibrated(double score, double expected)
    {
        Assert.Equal(expected, SpeakerAssigner.Calibrate(score), 3);
    }

    [Fact]
    public void ASegmentNoTurnTouchesHasNoSpeaker()
    {
        var segments = new[] { Segment("s1", 0, 5, "mic"), Segment("s2", 50, 55, "mic"), Segment("s3", 0, 5, "system") };

        var result = SpeakerAssigner.Assign(segments, [new DiarizedTrack("mic", [new SpeakerTurn(0, 5, 0, 0.9)])]);

        Assert.Equal(["spk1", null, null], result.Segments.Select(s => s.Speaker));
        Assert.Null(result.Segments[1].SpeakerConfidence);
    }

    /// <summary>A voice embedding pointing mostly along axis <paramref name="axis"/>, with a little of <paramref name="other"/>.</summary>
    private static SpeakerVoice Voice(int speaker, int axis, int other = -1) =>
        new(speaker, Enumerable.Range(0, 8).Select(i => i == axis ? 1f : i == other ? 0.3f : 0f).ToArray(), 20);

    /// <summary>The system track has readers A and B; the microphone picked both up from the loudspeakers.</summary>
    private static (TranscriptSegment[] Segments, DiarizedTrack[] Tracks) EchoedMeeting(bool voices = true)
    {
        var segments = new[]
        {
            Segment("s1", 0, 10, "system"), Segment("s2", 0.2, 10, "mic"),
            Segment("s3", 10, 20, "system"), Segment("s4", 10.2, 20, "mic"),
        };
        var tracks = new[]
        {
            new DiarizedTrack("system", [new SpeakerTurn(0, 10, 0, 0.7), new SpeakerTurn(10, 20, 1, 0.7)], voices ? [Voice(0, 0), Voice(1, 1)] : null),
            new DiarizedTrack("mic", [new SpeakerTurn(0.2, 10, 0, 0.6), new SpeakerTurn(10.2, 20, 1, 0.6)], voices ? [Voice(0, 0, 2), Voice(1, 1, 2)] : null),
        };
        return (segments, tracks);
    }

    [Fact]
    public void WithAnExpectedCountTheSameVoiceOnTwoTracksIsOnePerson()
    {
        var (segments, tracks) = EchoedMeeting();

        Assert.Equal(4, SpeakerAssigner.Assign(segments, tracks).Speakers.Count);
        var result = SpeakerAssigner.Assign(segments, tracks, expectedSpeakers: 2);

        Assert.Equal(["spk1", "spk1", "spk2", "spk2"], result.Segments.Select(s => s.Speaker));
        Assert.Equal(["Speaker 1", "Speaker 2"], result.Speakers.Select(s => s.Name));
        Assert.Equal(2, result.MergedAcrossTracks);
        Assert.Equal([19_800L, 19_800L], result.Speakers.Select(s => s.TalkTimeMs));
    }

    [Fact]
    public void TheExpectedCountIsReachedByJoiningTheMostAlikeVoicesFirst()
    {
        var (segments, tracks) = EchoedMeeting();

        // Down to 3: only the closest pair is joined; one count of 1 joins everyone.
        Assert.Equal(3, SpeakerAssigner.Assign(segments, tracks, expectedSpeakers: 3).Speakers.Count);
        Assert.Equal(["spk1", "spk1", "spk1", "spk1"], SpeakerAssigner.Assign(segments, tracks, expectedSpeakers: 1).Segments.Select(s => s.Speaker));
        // More expected than found changes nothing.
        Assert.Equal(4, SpeakerAssigner.Assign(segments, tracks, expectedSpeakers: 6).Speakers.Count);
    }

    [Fact]
    public void SpeakersWithoutAVoiceEmbeddingAreNeverJoined()
    {
        var (segments, tracks) = EchoedMeeting(voices: false);

        var result = SpeakerAssigner.Assign(segments, tracks, expectedSpeakers: 2);

        Assert.Equal(4, result.Speakers.Count);
        Assert.Equal(0, result.MergedAcrossTracks);
    }

    [Fact]
    public void TheTranscriptOrderIsKept()
    {
        var segments = new[] { Segment("s2", 10, 12, "system"), Segment("s1", 0, 2, "mic") };
        var tracks = new[] { new DiarizedTrack("mic", [new SpeakerTurn(0, 2, 0, 0.9)]), new DiarizedTrack("system", [new SpeakerTurn(10, 12, 0, 0.9)]) };

        var result = SpeakerAssigner.Assign(segments, tracks);

        Assert.Equal(["s2", "s1"], result.Segments.Select(s => s.Id));
        Assert.Equal(["spk2", "spk1"], result.Segments.Select(s => s.Speaker));
    }
}
