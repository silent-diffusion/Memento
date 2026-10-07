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
