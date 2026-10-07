using Memento.Core.Bridge.Contracts;
using Memento.Core.Workers;
using Memento.Transcription.Windows;

namespace Memento.Transcription.Tests;

public sealed class WindowTests
{
    [Fact]
    public void ARecordingShorterThanAWindowIsOneWindowKeepingEverything()
    {
        var window = Assert.Single(WindowPlanner.Plan(300));

        Assert.Equal((0, 300), (window.Start, window.End));
        Assert.Equal(double.NegativeInfinity, window.KeepFrom);
        Assert.Equal(double.PositiveInfinity, window.KeepTo);
    }

    [Fact]
    public void LongAudioIsTenMinuteWindowsOverlappingByFiveSecondsCutInTheMiddle()
    {
        var windows = WindowPlanner.Plan(1500);

        Assert.Equal(3, windows.Count);
        Assert.Equal([(0.0, 600.0), (595.0, 1195.0), (1190.0, 1500.0)], windows.Select(w => (w.Start, w.End)));
        Assert.Equal(597.5, windows[0].KeepTo);
        Assert.Equal(597.5, windows[1].KeepFrom);
        Assert.Equal(1192.5, windows[1].KeepTo);
        Assert.Equal(1192.5, windows[2].KeepFrom);
    }

    [Theory]
    [InlineData(600, 1)]
    [InlineData(605, 2)]
    [InlineData(606, 2)]
    [InlineData(7200, 13)]
    public void TheWindowCountCoversTheWholeTrack(double seconds, int count)
    {
        var windows = WindowPlanner.Plan(seconds);

        Assert.Equal(count, windows.Count);
        Assert.Equal(seconds, windows[^1].End);
        for (var i = 1; i < windows.Count; i++)
        {
            Assert.Equal(windows[i - 1].KeepTo, windows[i].KeepFrom);
            Assert.True(windows[i].Start < windows[i - 1].End);
        }
    }

    [Fact]
    public void NothingToPlanForAnEmptyTrack()
    {
        Assert.Empty(WindowPlanner.Plan(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => WindowPlanner.Plan(10, window: 5, overlap: 5));
    }

    /// <summary>A synthetic token stream: one word per second, "w0", "w1", …; each window transcribes its own range.</summary>
    private static List<WorkerSegment> Transcribe(double from, double to)
    {
        var words = new List<TranscriptWord>();
        for (var t = Math.Ceiling(from); t < to - 0.4; t++)
        {
            words.Add(new TranscriptWord($"w{t}", t, t + 0.4, 0.9));
        }

        return words.Chunk(4).Select(c => new WorkerSegment(c[0].S, c[^1].E, string.Join(' ', c.Select(w => w.W)), 0.9, c)).ToList();
    }

    [Fact]
    public void JoiningOverlappingWindowsKeepsEveryWordExactlyOnce()
    {
        var windows = WindowPlanner.Plan(1300);
        var kept = new List<WorkerSegment>();
        TranscriptWord? last = null;
        foreach (var window in windows)
        {
            var segments = SeamJoiner.Keep(Transcribe(window.Start, window.End), window.KeepFrom, window.KeepTo, last);
            last = segments.Count > 0 ? segments[^1].Words[^1] : last;
            kept.AddRange(segments);
        }

        var words = kept.SelectMany(s => s.Words).Select(w => w.W).ToList();
        Assert.Equal(words.Distinct().Count(), words.Count);
        Assert.Equal(Enumerable.Range(0, 1300).Select(i => $"w{i}"), words);
        Assert.All(kept, s => Assert.Equal(string.Join(' ', s.Words.Select(w => w.W)), s.Text));
    }

    [Fact]
    public void ASegmentStraddlingTheCutIsTrimmedToItsKeptWords()
    {
        var segment = new WorkerSegment(596, 599.4, "w596 w597 w598 w599", 0.5,
        [
            new TranscriptWord("w596", 596, 596.4, 0.9), new TranscriptWord("w597", 597, 597.4, 0.5),
            new TranscriptWord("w598", 598, 598.4, 0.8), new TranscriptWord("w599", 599, 599.4, 0.7),
        ]);

        var before = Assert.Single(SeamJoiner.Keep([segment], double.NegativeInfinity, 597.5, null));
        var after = Assert.Single(SeamJoiner.Keep([segment], 597.5, double.PositiveInfinity, null));

        Assert.Equal(("w596 w597", 596.0, 597.4, 0.5), (before.Text, before.Start, before.End, before.Confidence));
        Assert.Equal(("w598 w599", 598.0, 599.4, 0.7), (after.Text, after.Start, after.End, after.Confidence));
    }

    [Fact]
    public void AWordRepeatedAtTheSameMomentAcrossTheSeamIsDroppedOnce()
    {
        var previous = new TranscriptWord("budget.", 597.4, 597.9, 0.9);
        var next = new WorkerSegment(597.6, 599, "Budget is fine", 0.9,
            [new TranscriptWord("Budget", 597.6, 598, 0.9), new TranscriptWord("is", 598, 598.3, 0.9), new TranscriptWord("fine", 598.3, 599, 0.9)]);

        var kept = Assert.Single(SeamJoiner.Keep([next], 597.5, double.PositiveInfinity, previous));

        Assert.Equal("is fine", kept.Text);
    }

    [Fact]
    public void SegmentsWithoutWordsAreKeptByTheirStart()
    {
        var segments = new[] { new WorkerSegment(590, 596, "early", 0.9, []), new WorkerSegment(598, 600, "late", 0.9, []) };

        Assert.Equal(["early"], SeamJoiner.Keep(segments, double.NegativeInfinity, 597.5, null).Select(s => s.Text));
        Assert.Equal(["late"], SeamJoiner.Keep(segments, 597.5, double.PositiveInfinity, null).Select(s => s.Text));
    }
}
