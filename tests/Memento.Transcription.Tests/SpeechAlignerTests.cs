using Memento.Core.Bridge.Contracts;
using Memento.Core.Workers;
using Memento.Transcription.Windows;

namespace Memento.Transcription.Tests;

public sealed class SpeechAlignerTests
{
    private static readonly IReadOnlyList<(double Start, double End)> Sound = [(1.5, 4.35), (6.85, 7.4), (8.13, 10.23), (14.23, 18.0)];

    private static WorkerSegment Line(double start, double end, params (string W, double S, double E)[] words) =>
        new(start, end, string.Join(' ', words.Select(w => w.W)), 0.9, words.Select(w => new TranscriptWord(w.W, w.S, w.E, 0.9)).ToList());

    [Fact]
    public void AStartInThePauseMovesToWhereTheVoiceStarts()
    {
        // Whisper put the line at 11.0, the end of the pause before it; the voice starts at 14.23.
        var aligned = SpeechAligner.Align(Line(11.0, 19.0, ("Great.", 11.0, 12.0), ("Let", 12.0, 19.0)), Sound, 0);

        Assert.Equal(14.23, aligned.Start);
        Assert.Equal(18.0, aligned.End);
        Assert.Equal([14.23, 14.23], aligned.Words.Select(w => w.S));
        Assert.Equal([14.23, 18.0], aligned.Words.Select(w => w.E));
    }

    [Fact]
    public void ALineInsideItsSoundKeepsItsTimes()
    {
        var line = Line(1.5, 4.35, ("Good", 1.5, 1.9));

        Assert.Same(line, SpeechAligner.Align(line, Sound, 0));
    }

    [Fact]
    public void AStartInTheTailOfThePreviousSoundMovesToTheNextSound()
    {
        // 0.2 s before the end of the 1.5–4.35 sound, and the line runs on into the next one.
        var aligned = SpeechAligner.Align(Line(4.15, 7.3), Sound, 0);

        Assert.Equal(6.85, aligned.Start);
        Assert.Equal(7.3, aligned.End);
    }

    [Fact]
    public void AStartNearTheBeginningOfAShortSoundStaysInIt()
    {
        // "Morning." is 6.85–7.4; a start 0.05 s into it is that word, not the tail of something before.
        var aligned = SpeechAligner.Align(Line(6.9, 10.2), Sound, 0);

        Assert.Equal(6.9, aligned.Start);
    }

    [Fact]
    public void AnEndInTheHeadOfTheNextSoundMovesBackToWhereTheLineStopped()
    {
        var aligned = SpeechAligner.Align(Line(1.5, 7.0), Sound, 0);

        Assert.Equal(1.5, aligned.Start);
        Assert.Equal(4.35, aligned.End);
    }

    [Fact]
    public void ALineWithNoSoundUnderItKeepsItsTimes()
    {
        var line = Line(11.0, 13.0);

        Assert.Same(line, SpeechAligner.Align(line, Sound, 0));
        Assert.Same(line, SpeechAligner.Align(line, [], 0));
    }

    [Fact]
    public void TheTrackOffsetIsTakenIntoAccount()
    {
        var aligned = SpeechAligner.Align(Line(111.0, 119.0), Sound, 100);

        Assert.Equal(114.23, aligned.Start);
        Assert.Equal(118.0, aligned.End);
    }
}
