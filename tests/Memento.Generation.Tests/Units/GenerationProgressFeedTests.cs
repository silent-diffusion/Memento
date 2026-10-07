using System.Collections.Concurrent;
using System.Globalization;
using Memento.Core.Bridge.Contracts;
using Memento.Generation.Generation;

namespace Memento.Generation.Tests.Units;

/// <summary>
/// <c>generation.progress</c> reaches the UI in the order it was raised, throttled inside that order, with the final
/// event last (the defect behind a CI failure: callbacks posted to the thread pool overtook each other, so
/// "verifying" arrived before "generating").
/// </summary>
public sealed class GenerationProgressFeedTests
{
    private static readonly string[] Stages = ["composing", "generating", "verifying", "rendering"];

    [Fact]
    public async Task HundredRapidEventsFromManyThreadsArriveInTheOrderTheyWereRaised()
    {
        var delivered = new ConcurrentQueue<GenerationProgress>();
        var feed = new GenerationProgressFeed(delivered.Enqueue, new SteppingTime(TimeSpan.FromSeconds(1)));
        var raised = new List<int>();
        var gate = new object();
        var next = 0;
        var threads = Enumerable.Range(0, 8).Select(_ => new Thread(() =>
        {
            while (true)
            {
                // The gate defines the raise order; each thread then reports straight away, as the pipeline does.
                lock (gate)
                {
                    if (next == 100)
                    {
                        return;
                    }

                    var n = next++;
                    raised.Add(n);
                    Assert.True(feed.Report(Event(Stages[n * Stages.Length / 100], n)));
                }

                Thread.Yield();
            }
        })).ToList();
        threads.ForEach(t => t.Start());
        threads.ForEach(t => t.Join());

        await feed.CompleteAsync(Event("done", 100));

        var percents = delivered.Select(e => (int)e.Percent).ToList();
        Assert.Equal([.. raised, 100], percents);
        Assert.Equal("done", delivered.Last().Stage);
        Assert.False(feed.Report(Event("rendering", 101)));
        Assert.Equal(101, delivered.Count);
    }

    [Fact]
    public async Task InlineReportsFromConcurrentWorkersStayInOrderThroughTheThrottle()
    {
        // Time stands still: only the first event of each stage and the final one may pass, in raise order.
        var delivered = new ConcurrentQueue<GenerationProgress>();
        var feed = new GenerationProgressFeed(delivered.Enqueue, new SteppingTime(TimeSpan.Zero));
        var gate = new object();
        var counter = 0;
        var progress = new InlineProgress<int>(n => feed.Report(Event(Stages[n * Stages.Length / 100], n)));
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 25; i++)
            {
                lock (gate)
                {
                    progress.Report(counter++);
                }
            }
        })));
        await feed.CompleteAsync(Event("done", 100));

        Assert.Equal(["composing", "generating", "verifying", "rendering", "done"], delivered.Select(e => e.Stage));
        Assert.Equal([0, 25, 50, 75, 100], delivered.Select(e => (int)e.Percent));
    }

    [Fact]
    public async Task TheThrottleLetsAtMostFourASecondThroughButNeverDropsAStageChangeOrTheFinal()
    {
        var delivered = new ConcurrentQueue<GenerationProgress>();
        var time = new SteppingTime(TimeSpan.FromMilliseconds(100));
        var feed = new GenerationProgressFeed(delivered.Enqueue, time);
        for (var i = 0; i < 40; i++)
        {
            feed.Report(Event(i < 20 ? "generating" : "verifying", i));
        }

        Assert.True(feed.Report(Event("verifying", 40), force: true));
        await feed.CompleteAsync(Event("failed", 0));
        await feed.CompleteAsync(Event("done", 100));

        var stages = delivered.Select(e => e.Stage).ToList();
        Assert.Equal("failed", stages[^1]);
        Assert.DoesNotContain("done", stages);
        Assert.Contains(delivered, e => e.Stage == "verifying" && (int)e.Percent == 20);
        Assert.Contains(delivered, e => (int)e.Percent == 40);
        Assert.True(delivered.Count < 20, string.Create(CultureInfo.InvariantCulture, $"{delivered.Count} events passed the throttle."));
        var percents = delivered.Take(delivered.Count - 1).Select(e => e.Percent).ToList();
        Assert.Equal(percents.Order(), percents);
    }

    private static GenerationProgress Event(string stage, int n) => new("g1", "r1", null, stage, null, n, null);

    /// <summary>A clock that moves on by a fixed step every time it is read.</summary>
    private sealed class SteppingTime(TimeSpan step) : TimeProvider
    {
        private long _now = 1;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Interlocked.Add(ref _now, step.Ticks);
    }
}
