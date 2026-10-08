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
        Assert.Equal(2, result.Merged);
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
    public void WithoutVoiceEmbeddingsTheCountStillHolds()
    {
        var (segments, tracks) = EchoedMeeting(voices: false);

        var result = SpeakerAssigner.Assign(segments, tracks, expectedSpeakers: 2);

        // Nothing to compare by: each voiceless voice goes into the voice with the most speech on its track.
        Assert.Equal(2, result.Speakers.Count);
        Assert.Equal(2, result.Merged);
        Assert.Equal(["spk1", "spk2", "spk1", "spk2"], result.Segments.Select(s => s.Speaker));
    }

    [Fact]
    public void WithoutVoiceEmbeddingsAutoJoinsNothing()
    {
        var (segments, tracks) = EchoedMeeting(voices: false);

        var result = SpeakerAssigner.Assign(segments, tracks, null, joinSimilarity: 0.5, minSpeechSeconds: 30, foldSimilarity: 0.3);

        Assert.Equal(4, result.Speakers.Count);
        Assert.Equal(0, result.Merged);
    }

    /// <summary>
    /// One track: readers A and B (who sound somewhat alike, 0.6) talk for 40 and 30 seconds; the diarizer also made a
    /// cluster of a 3-second laugh that sounds like nobody, and split 12 seconds of A off as a cluster of its own.
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

    [Fact]
    public void LittleVoicesAreFoldedInLastSoTheMainVoicesAreComparedWithEachOther()
    {
        var (segments, tracks) = SplitMeeting();

        // With the laugh among the main voices, A and B are the most alike pair once A is whole, so they would be joined.
        var naive = SpeakerAssigner.Assign(segments, tracks, expectedSpeakers: 2, minSpeechSeconds: 0);
        Assert.Equal(naive.Segments[0].Speaker, naive.Segments[1].Speaker);
        var result = SpeakerAssigner.Assign(segments, tracks, expectedSpeakers: 2, minSpeechSeconds: 10);

        Assert.Equal(["spk1", "spk2", "spk1", "spk1", "spk2", "spk1"], result.Segments.Select(s => s.Speaker));
        Assert.Equal(2, result.Merged);
    }

    [Fact]
    public void ACountIsNeverReachedBySplitting()
    {
        var (segments, tracks) = SplitMeeting();

        var result = SpeakerAssigner.Assign(segments, tracks, expectedSpeakers: 6, minSpeechSeconds: 10);

        Assert.Equal(4, result.Speakers.Count);
        Assert.Equal(0, result.Merged);
    }

    [Fact]
    public void InAutoVoicesOnOneTrackThatSoundAlikeAreOnePerson()
    {
        var (segments, tracks) = SplitMeeting();

        var joined = SpeakerAssigner.Assign(segments, tracks, null, joinSimilarity: 0.8, minSpeechSeconds: 10, foldSimilarity: 0.95);
        var apart = SpeakerAssigner.Assign(segments, tracks, null, joinSimilarity: 0.999, minSpeechSeconds: 10, foldSimilarity: 0.95);

        // A's split-off part (similarity 0.95) joins A, B (0.6) does not; the laugh is not like anyone enough and stays.
        Assert.Equal(["spk1", "spk2", "spk3", "spk1", "spk2", "spk1"], joined.Segments.Select(s => s.Speaker));
        Assert.Equal(4, apart.Speakers.Count);
    }

    [Fact]
    public void InAutoVoicesOfDifferentTracksAreNeverJoined()
    {
        var (segments, tracks) = EchoedMeeting();

        var result = SpeakerAssigner.Assign(segments, tracks, null, joinSimilarity: 0.1, minSpeechSeconds: 0, foldSimilarity: 0.1);

        Assert.Equal(4, result.Speakers.Count);
    }

    [Fact]
    public void EveryVoiceIsKeptWithItsTrackEmbeddingAndLines()
    {
        var (segments, tracks) = SplitMeeting();

        var result = SpeakerAssigner.Assign(segments, tracks, expectedSpeakers: 2, minSpeechSeconds: 10);

        var voices = result.Voices!;
        Assert.Equal([0, 1, 2, 3], voices.Select(v => v.Speaker));
        Assert.All(voices, v => Assert.Equal("mic", v.Track));
        Assert.Equal(["a1", "a2"], voices[0].SegmentIds);
        Assert.Equal(["l1"], voices[2].SegmentIds);
        Assert.Equal([1f, 0f, 0f], voices[0].Embedding);
        Assert.Equal(40, voices[0].Seconds);
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
