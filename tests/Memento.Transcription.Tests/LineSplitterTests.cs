using Memento.Core.Bridge.Contracts;
using Memento.Core.Workers;
using Memento.Transcription.Speakers;

namespace Memento.Transcription.Tests;

public sealed class LineSplitterTests
{
    /// <summary>Two speakers on the mic track: 0 until 10 s, then 1.</summary>
    private static readonly DiarizedTrack Mic = new("mic", [new SpeakerTurn(0, 10, 0, 0.7), new SpeakerTurn(10, 20, 1, 0.65)]);

    private static TranscriptWord Word(string text, double start, double end, double confidence = 0.9) => new(text, start, end, confidence);

    private static TranscriptSegment Line(string id, double start, double end, IReadOnlyList<TranscriptWord> words, string? track = "mic", TranscriptEdit? edited = null) =>
        new(id, start, end, track, null, null, string.Join(' ', words.Select(w => w.W)), words.Count == 0 ? 0.9 : words.Min(w => w.C), words, edited);

    /// <summary>"Yes, sure." said by 0 until 9.6 s, then "So what then?" by 1.</summary>
    private static TranscriptSegment Exchange(string id = "s0001") =>
        Line(id, 8.0, 13.0, [Word("Yes,", 8.1, 8.6, 0.8), Word("sure.", 8.7, 9.6, 0.95), Word("So", 10.2, 10.5, 0.7), Word("what", 10.5, 10.9, 0.9), Word("then?", 10.9, 12.8, 0.85)]);

    [Fact]
    public void ALineWhoseWordsFallUnderTwoSpeakersIsCutBetweenThem()
    {
        var result = LineSplitter.Split([Exchange()], [Mic]);

        Assert.Equal(1, result.Split);
        var (first, second) = (result.Segments[0], result.Segments[1]);
        Assert.Equal(2, result.Segments.Count);
        Assert.Equal(("s0001", "s0001-2"), (first.Id, second.Id));
        Assert.Equal(("Yes, sure.", "So what then?"), (first.Text, second.Text));
        // The first part keeps the line's start and the last its end; the cut falls between the words.
        Assert.Equal((8.0, 9.6), (first.Start, first.End));
        Assert.Equal((10.2, 13.0), (second.Start, second.End));
        Assert.Equal((0.8, 0.7), (first.Confidence, second.Confidence));
        Assert.Equal(["Yes,", "sure."], first.Words.Select(w => w.W));
        Assert.Equal(["So", "what", "then?"], second.Words.Select(w => w.W));
        Assert.All(result.Segments, s => Assert.Equal("mic", s.Track));
        Assert.All(result.Segments, s => Assert.Null(s.Speaker));
        Assert.All(result.Segments, s => Assert.Null(s.Edited));
    }

    [Fact]
    public void TheAssignerThenGivesEachPartItsOwnSpeaker()
    {
        var cut = LineSplitter.Split([Exchange()], [Mic]);

        var assigned = SpeakerAssigner.Assign(cut.Segments, [Mic]);

        Assert.Equal(["spk1", "spk2"], assigned.Segments.Select(s => s.Speaker));
    }

    [Fact]
    public void AOneWordTailStaysOnTheLine()
    {
        var line = Line("s1", 8.0, 11.0, [Word("Yes,", 8.1, 8.6), Word("sure.", 8.7, 9.6), Word("Okay.", 10.2, 10.9)]);

        var result = LineSplitter.Split([line], [Mic]);

        Assert.Equal(0, result.Split);
        Assert.Same(line, Assert.Single(result.Segments));
    }

    [Fact]
    public void ATailUnderHalfASecondStaysOnTheLine()
    {
        var line = Line("s1", 8.0, 11.0, [Word("Yes,", 8.1, 8.6), Word("sure.", 8.7, 9.6), Word("Oh", 10.2, 10.3), Word("no.", 10.3, 10.6)]);

        var result = LineSplitter.Split([line], [Mic]);

        Assert.Equal(0, result.Split);
        Assert.Same(line, Assert.Single(result.Segments));
    }

    [Fact]
    public void ALineTheUserEditedIsNeverCut()
    {
        var line = Exchange() with { Edited = new TranscriptEdit(DateTimeOffset.UnixEpoch, "Yes sure so what then") };

        var result = LineSplitter.Split([line], [Mic]);

        Assert.Equal(0, result.Split);
        Assert.Same(line, Assert.Single(result.Segments));
    }

    [Fact]
    public void ALineWithoutWordsOrOnAnUnheardTrackIsNeverCut()
    {
        var noWords = Line("s1", 8.0, 13.0, []);
        var otherTrack = Exchange("s2") with { Track = "system" };
        var mix = Exchange("s3") with { Track = null };

        var result = LineSplitter.Split([noWords, otherTrack, mix], [Mic]);

        Assert.Equal(0, result.Split);
        Assert.Equal([noWords, otherTrack, mix], result.Segments);
    }

    [Fact]
    public void AWordNoTurnTouchesJoinsTheWordBeforeIt()
    {
        // The diarizer heard nothing between 9.6 and 10.2 s; "um" there belongs to the first speaker's run.
        var track = new DiarizedTrack("mic", [new SpeakerTurn(0, 9.6, 0, 0.7), new SpeakerTurn(10.2, 20, 1, 0.65)]);
        var line = Line("s1", 8.0, 13.0, [Word("Yes,", 8.1, 8.6), Word("sure,", 8.7, 9.5), Word("um", 9.7, 10.1), Word("so", 10.2, 10.5), Word("what", 10.5, 10.9), Word("then?", 10.9, 12.8)]);

        var result = LineSplitter.Split([line], [track]);

        Assert.Equal(("Yes, sure, um", "so what then?"), (result.Segments[0].Text, result.Segments[1].Text));
    }

    [Fact]
    public void ChildIdsNeverCollideWithIdsTheTranscriptHas()
    {
        var taken = Line("s0001-2", 30.0, 31.0, [Word("Later.", 30.1, 30.9)]);

        var result = LineSplitter.Split([Exchange(), taken], [Mic]);

        Assert.Equal(["s0001", "s0001-3", "s0001-2"], result.Segments.Select(s => s.Id));
    }

    [Fact]
    public void OrderIsKeptAroundTheCutLine()
    {
        var before = Line("s0001", 1.0, 3.0, [Word("Morning.", 1.1, 2.9)]);
        var after = Line("s0003", 14.0, 16.0, [Word("Right.", 14.1, 15.9)]);

        var result = LineSplitter.Split([before, Exchange("s0002"), after], [Mic]);

        Assert.Equal(["s0001", "s0002", "s0002-2", "s0003"], result.Segments.Select(s => s.Id));
        Assert.Equal(1, result.Split);
    }

    [Fact]
    public void ThreeSpeakersInOneLineGiveThreeParts()
    {
        var track = new DiarizedTrack("mic", [new SpeakerTurn(0, 10, 0, 0.7), new SpeakerTurn(10, 12, 2, 0.6), new SpeakerTurn(12, 20, 1, 0.65)]);
        var line = Line("s1", 8.0, 15.0, [Word("Yes,", 8.1, 8.6), Word("sure.", 8.7, 9.6), Word("Hold", 10.2, 10.6), Word("on.", 10.6, 11.4), Word("So", 12.2, 12.5), Word("what", 12.5, 12.9), Word("then?", 12.9, 14.8)]);

        var result = LineSplitter.Split([line], [track]);

        Assert.Equal(1, result.Split);
        Assert.Equal(["s1", "s1-2", "s1-3"], result.Segments.Select(s => s.Id));
        Assert.Equal(["Yes, sure.", "Hold on.", "So what then?"], result.Segments.Select(s => s.Text));
        Assert.Equal([(8.0, 9.6), (10.2, 11.4), (12.2, 15.0)], result.Segments.Select(s => (s.Start, s.End)));
    }

    [Fact]
    public void AShortRunInTheMiddleFoldsIntoTheNeighbourWithMoreSpeechAndRunsThatThenMatchJoin()
    {
        // "no" alone is speaker 1's for 0.3 s between two stretches of speaker 0: it folds into the longer one (after), and the
        // runs on both sides then share a speaker, so the line stays one line.
        var track = new DiarizedTrack("mic", [new SpeakerTurn(0, 9.6, 0, 0.7), new SpeakerTurn(9.6, 9.9, 1, 0.6), new SpeakerTurn(9.9, 20, 0, 0.7)]);
        var line = Line("s1", 8.0, 13.0, [Word("Yes,", 8.1, 8.6), Word("sure.", 8.7, 9.5), Word("no", 9.6, 9.9), Word("so", 10.0, 10.5), Word("what", 10.5, 10.9), Word("then?", 10.9, 12.8)]);

        var result = LineSplitter.Split([line], [track]);

        Assert.Equal(0, result.Split);
        Assert.Same(line, Assert.Single(result.Segments));
    }
}
