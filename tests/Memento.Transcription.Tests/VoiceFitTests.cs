using Memento.Core.Bridge.Contracts;
using Memento.Core.Workers;
using Memento.Transcription.Speakers;

namespace Memento.Transcription.Tests;

/// <summary>
/// The voice fit (2.6.0, ENGINE-NOTES.md §V): a raw voice grouped with others is compared with the mix of its person's
/// other voices, and its lines are marked uncertain when it fits poorly and those others are the larger part of the person.
/// </summary>
public sealed class VoiceFitTests
{
    private static TranscriptSegment Segment(string id, double start, double end) => new(id, start, end, "mic", null, null, id, 1, [], null);

    /// <summary>A unit embedding at the given cosine to the first axis.</summary>
    private static float[] At(double cosine) => [(float)cosine, (float)Math.Sqrt(1 - (cosine * cosine)), 0f];

    /// <summary>
    /// One track: voice A (along the first axis) talks for 40 s, voice B (at <paramref name="cosine"/> to A) for
    /// <paramref name="bSeconds"/>, then A again for 17 s.
    /// </summary>
    private static (TranscriptSegment[] Segments, DiarizedTrack[] Tracks) Meeting(double bSeconds, double cosine, double bTurnConfidence = 0.9)
    {
        var bEnd = 40 + bSeconds;
        var segments = new[] { Segment("a1", 0, 40), Segment("b1", 40, bEnd), Segment("a2", bEnd, bEnd + 17) };
        var turns = new[] { new SpeakerTurn(0, 40, 0, 0.9), new SpeakerTurn(40, bEnd, 1, bTurnConfidence), new SpeakerTurn(bEnd, bEnd + 17, 0, 0.9) };
        SpeakerVoice[] voices = [new(0, [1f, 0f, 0f], 57), new(1, At(cosine), bSeconds)];
        return (segments, [new DiarizedTrack("mic", turns, voices, bEnd + 17)]);
    }

    private static SpeakerAssigner.Result Auto(TranscriptSegment[] segments, DiarizedTrack[] tracks, IReadOnlyList<SpeakerPin>? pins = null) =>
        SpeakerAssigner.Assign(segments, tracks, null, TranscriptionDefaults.JoinSimilarity, TranscriptionDefaults.MinSpeakerSeconds, TranscriptionDefaults.FoldSimilarity, TranscriptionDefaults.OwnSpeakerSeconds, pins);

    [Fact]
    public void ALittleVoiceFoldedIntoAnUnlikeVoiceMarksItsLinesAndNotTheSpeakers()
    {
        // A 2-second voice at cosine 0.3 to A is folded into A (Auto: under 10 s it always joins a main voice).
        var (segments, tracks) = Meeting(2, 0.3);

        var result = Auto(segments, tracks);

        Assert.Equal(["spk1"], result.Speakers.Select(s => s.Id));
        Assert.Equal(["spk1", "spk1", "spk1"], result.Segments.Select(s => s.Speaker));
        Assert.Equal(1.0, result.Segments[0].SpeakerConfidence);
        Assert.Equal(1.0, result.Segments[2].SpeakerConfidence);
        Assert.Equal(0.25, result.Segments[1].SpeakerConfidence!.Value, 3);
        Assert.Equal(1, result.Doubtful);
        // Both fits are kept in voices.json; only the little voice's lines were lowered (A is most of the person).
        Assert.Equal([0.3, 0.3], result.Voices!.Select(v => Math.Round(v.Fit!.Value, 2)));
    }

    [Fact]
    public void VoicesJoinedToReachACountThatSoundAlikeChangeNothing()
    {
        var (segments, tracks) = Meeting(30, 0.7);

        var result = SpeakerAssigner.Assign(segments, tracks, expectedSpeakers: 1);

        Assert.Equal(["spk1", "spk1", "spk1"], result.Segments.Select(s => s.Speaker));
        Assert.All(result.Segments, s => Assert.Equal(1.0, s.SpeakerConfidence));
        Assert.Equal(0, result.Doubtful);
        Assert.Equal([0.7, 0.7], result.Voices!.Select(v => Math.Round(v.Fit!.Value, 2)));
    }

    [Fact]
    public void TwoEqualVoicesJoinedToReachACountThatSoundUnlikeAreBothDoubtful()
    {
        var segments = new[] { Segment("a1", 0, 30), Segment("b1", 30, 60) };
        var tracks = new[]
        {
            new DiarizedTrack("mic", [new SpeakerTurn(0, 30, 0, 0.9), new SpeakerTurn(30, 60, 1, 0.9)], [new SpeakerVoice(0, [1f, 0f, 0f], 30), new SpeakerVoice(1, At(0.3), 30)], 60),
        };

        var result = SpeakerAssigner.Assign(segments, tracks, expectedSpeakers: 1);

        Assert.Equal(["spk1", "spk1"], result.Segments.Select(s => s.Speaker));
        Assert.All(result.Segments, s => Assert.Equal(0.25, s.SpeakerConfidence!.Value, 3));
        Assert.Equal(2, result.Doubtful);
    }

    [Fact]
    public void TheLargerVoiceOfAPersonIsNeverDoubtedByTheSmaller()
    {
        var segments = new[] { Segment("a1", 0, 36), Segment("b1", 36, 60) };
        var tracks = new[]
        {
            new DiarizedTrack("mic", [new SpeakerTurn(0, 36, 0, 0.9), new SpeakerTurn(36, 60, 1, 0.9)], [new SpeakerVoice(0, [1f, 0f, 0f], 36), new SpeakerVoice(1, At(0.3), 24)], 60),
        };

        var result = SpeakerAssigner.Assign(segments, tracks, expectedSpeakers: 1);

        Assert.Equal(1.0, result.Segments[0].SpeakerConfidence);
        Assert.Equal(0.25, result.Segments[1].SpeakerConfidence!.Value, 3);
        Assert.Equal(1, result.Doubtful);
    }

    [Fact]
    public void APersonWithOneVoiceIsNotChecked()
    {
        var segments = new[] { Segment("a1", 0, 40) };
        var tracks = new[] { new DiarizedTrack("mic", [new SpeakerTurn(0, 40, 0, 0.9)], [new SpeakerVoice(0, [1f, 0f, 0f], 40)], 40) };

        var result = Auto(segments, tracks);

        Assert.Equal(1.0, Assert.Single(result.Segments).SpeakerConfidence);
        Assert.Null(Assert.Single(result.Voices!).Fit);
        Assert.Equal(0, result.Doubtful);
    }

    [Fact]
    public void AVoiceWithoutAnEmbeddingIsNotChecked()
    {
        // The second cluster has no voice: it joins A to reach the count, and nothing can be said about its fit.
        var segments = new[] { Segment("a1", 0, 40), Segment("v1", 40, 45) };
        var tracks = new[]
        {
            new DiarizedTrack("mic", [new SpeakerTurn(0, 40, 0, 0.9), new SpeakerTurn(40, 45, 1, 0.9)], [new SpeakerVoice(0, [1f, 0f, 0f], 40)], 45),
        };

        var result = SpeakerAssigner.Assign(segments, tracks, expectedSpeakers: 1);

        Assert.Equal(["spk1", "spk1"], result.Segments.Select(s => s.Speaker));
        Assert.All(result.Segments, s => Assert.Equal(1.0, s.SpeakerConfidence));
        Assert.All(result.Voices!, v => Assert.Null(v.Fit));
        Assert.Equal(0, result.Doubtful);
    }

    [Fact]
    public void APlacedLineInADoubtfulVoiceKeepsItsConfidence()
    {
        var (segments, tracks) = Meeting(2, 0.3);

        var result = SpeakerAssigner.Assign(segments, tracks, expectedSpeakers: 1, pins: [new SpeakerPin("b1", "Ana", true)]);

        Assert.Equal(["spk1", "spk1", "spk1"], result.Segments.Select(s => s.Speaker));
        var placed = result.Segments[1];
        Assert.Equal(1.0, placed.SpeakerConfidence);
        Assert.True(placed.SpeakerByUser);
        Assert.Equal(0, result.Doubtful);
        Assert.Equal(0.3, result.Voices![1].Fit!.Value, 2);
    }

    [Fact]
    public void LinesAlreadyUncertainByTheirTurnsAreNotCountedAgain()
    {
        // B's turn scored 0.3 (calibrated 0.25) before the fit said anything, so the fit marks nothing new.
        var (segments, tracks) = Meeting(2, 0.3, bTurnConfidence: 0.3);

        var result = Auto(segments, tracks);

        Assert.Equal(0.25, result.Segments[1].SpeakerConfidence!.Value, 3);
        Assert.Equal(0, result.Doubtful);
    }
}
