using System.Collections.Concurrent;
using System.Text.Json;
using Memento.AI;
using Memento.AI.Local;
using Memento.Core.Bridge.Contracts;
using Memento.Generation.Generation;

namespace Memento.Generation.Tests.Units;

/// <summary>
/// <c>generation.output</c> (the Live output sheet): ordered, token text coalesced to about ten events a second without
/// losing a character, every reply after its tokens, <c>done</c> last. No wall clock: the feed reads a stepping clock.
/// </summary>
public sealed class GenerationOutputFeedTests
{
    private static readonly AiRequest Request = AiRequest.Create("map.commitments#c1", "Extract decisions.", "[1] Ana: We ship on Thursday.", 300);

    [Fact]
    public async Task WhileTimeStandsStillTheTokensOfAPassArriveAsTheFirstPieceAndTheRestBeforeTheReply()
    {
        var delivered = new ConcurrentQueue<GenerationOutput>();
        var feed = new GenerationOutputFeed("g1", delivered.Enqueue, new SteppingTime(TimeSpan.Zero));

        var id = feed.Request(new OutputPass(GenerationOutputFeed.MapStep, "Decisions and action items · segment 1 of 1"), Request, streamed: true);
        var pieces = Enumerable.Range(0, 100).Select(i => $"t{i} ").ToList();
        for (var i = 0; i < pieces.Count; i++)
        {
            feed.Tokens(id, pieces[i], i + 1, TimeSpan.FromMilliseconds(20 * (i + 1)));
        }

        feed.Answered(id, 100, new AiAnswerFacts("eog", 42, 51.5));
        feed.Reply(id, Response(string.Concat(pieces)));
        await feed.CompleteAsync();

        var events = delivered.ToList();
        Assert.Equal(["request", "token", "token", "reply", "done"], events.Select(e => e.Kind));
        Assert.Equal(string.Concat(pieces), string.Concat(events.Where(e => e.Kind == "token").Select(e => e.Text)));
        var request = events[0];
        Assert.Equal(("p1", "map", "Decisions and action items · segment 1 of 1", true), (request.PassId, request.Step, request.Title, request.Streamed));
        Assert.Equal(AiRequestText.Render(Request), request.Text);
        Assert.Equal("System\nExtract decisions.\n\nUser\n[1] Ana: We ship on Thursday.", request.Text);
        Assert.Equal(100, events[2].OutputTokens);
        Assert.Equal(2000, events[2].ElapsedMs);
        Assert.Equal(49.5, events[2].TokensPerSecond);
        var reply = events[3];
        Assert.Equal((string.Concat(pieces), 100, 42, 51.5, "eog"), (reply.Text, reply.OutputTokens, reply.PromptTokens, reply.TokensPerSecond, reply.StopReason));
        Assert.Equal(("g1", string.Empty, string.Empty), (events[^1].JobId, events[^1].PassId, events[^1].Text));
    }

    [Fact]
    public async Task TokensArrivingEveryTwentyMillisecondsGoOutAboutTenTimesASecond()
    {
        var delivered = new ConcurrentQueue<GenerationOutput>();
        var feed = new GenerationOutputFeed("g1", delivered.Enqueue, new SteppingTime(TimeSpan.FromMilliseconds(20)));
        var id = feed.Request(new OutputPass("map", "Summary · segment 1 of 2"), Request, streamed: true);

        for (var i = 0; i < 100; i++)
        {
            feed.Tokens(id, "x", i + 1, null);
        }

        feed.Answered(id, 100, new AiAnswerFacts("eog", 10, 50));
        await feed.CompleteAsync();

        var tokens = delivered.Where(e => e.Kind == "token").ToList();
        // 100 pieces over about two seconds of the feed's clock: one event per 100 ms, give or take the first and last.
        Assert.InRange(tokens.Count, 15, 30);
        Assert.Equal(new string('x', 100), string.Concat(tokens.Select(e => e.Text)));
        Assert.Equal(tokens.Select(e => e.OutputTokens).Order(), tokens.Select(e => e.OutputTokens));
        Assert.Null(tokens[0].TokensPerSecond);
    }

    [Fact]
    public async Task EventsFromManyThreadsStayInTheOrderTheyWereRaisedAndNothingFollowsDone()
    {
        var delivered = new ConcurrentQueue<GenerationOutput>();
        var feed = new GenerationOutputFeed("g1", delivered.Enqueue, new SteppingTime(TimeSpan.FromMilliseconds(1)));
        var gate = new object();
        var raised = new List<string>();
        await Task.WhenAll(Enumerable.Range(0, 4).Select(t => Task.Run(() =>
        {
            for (var i = 0; i < 10; i++)
            {
                lock (gate)
                {
                    // Cloud passes run a few at a time: each request and its reply are raised from a worker thread.
                    var id = feed.Request(new OutputPass("verify", $"Check {t}.{i}"), Request, streamed: false);
                    feed.Reply(id, Response($"answer {t}.{i}"));
                    raised.Add(id);
                }
            }
        })));
        await feed.CompleteAsync();
        feed.Step("grounding", "late", "after done", TimeSpan.Zero);
        feed.Reply("p1", Response("late"));
        await feed.Completion;

        var events = delivered.ToList();
        Assert.Equal(raised, events.Where(e => e.Kind == "request").Select(e => e.PassId));
        Assert.Equal(raised, events.Where(e => e.Kind == "reply").Select(e => e.PassId));
        Assert.All(events.Where(e => e.Kind == "request"), e => Assert.False(e.Streamed));
        Assert.Equal("done", events[^1].Kind);
        Assert.Equal(81, events.Count);
    }

    [Fact]
    public async Task AReplyIsSentOncePerPassAndTheElapsedTimeIsTheFeedsOwn()
    {
        var delivered = new ConcurrentQueue<GenerationOutput>();
        var time = new SteppingTime(TimeSpan.Zero);
        var feed = new GenerationOutputFeed("g1", delivered.Enqueue, time);

        feed.Step("segment", "Segment the transcript", "2 segments", TimeSpan.FromMilliseconds(3));
        var cloud = feed.Request(new OutputPass("map", "Quotes · segment 1 of 2"), Request, streamed: false);
        time.Advance(TimeSpan.FromSeconds(4));
        feed.Reply(cloud, Response("{\"quotes\":[]}"));
        feed.Reply(cloud, Response("again"));
        feed.Tokens(cloud, "late", 1, null);
        feed.Tokens("p99", "unknown pass", 1, null);
        await feed.CompleteAsync();

        var events = delivered.ToList();
        Assert.Equal(["step", "request", "reply", "done"], events.Select(e => e.Kind));
        Assert.Equal(("p1", "segment", "2 segments", 3L), (events[0].PassId, events[0].Step, events[0].Text, events[0].ElapsedMs));
        Assert.Equal("p2", events[1].PassId);
        Assert.Equal(("{\"quotes\":[]}", 4000L, "end_turn"), (events[2].Text, events[2].ElapsedMs, events[2].StopReason));
        Assert.Null(events[2].TokensPerSecond);
    }

    [Fact]
    public void TheEventIsSerializedWithEveryField()
    {
        var json = Memento.Core.Bridge.BridgeEventPublisher.Serialize(
            Memento.Core.Bridge.BridgeEventNames.GenerationOutput,
            new GenerationOutput("g1", "p3", "reply", null, null, "{}") { OutputTokens = 2, PromptTokens = 40, TokensPerSecond = 51.5, ElapsedMs = 900, StopReason = "eog" },
            Memento.Core.Bridge.M4BridgeJsonContext.Default.BridgeEventEnvelopeGenerationOutput);

        using var document = JsonDocument.Parse(json);
        Assert.Equal("generation.output", document.RootElement.GetProperty("event").GetString());
        Assert.Equal(
            """{"jobId":"g1","passId":"p3","kind":"reply","step":null,"title":null,"text":"{}","streamed":null,"outputTokens":2,"promptTokens":40,"tokensPerSecond":51.5,"elapsedMs":900,"stopReason":"eog"}""",
            document.RootElement.GetProperty("payload").GetRawText());
    }

    private static AiResponse Response(string text) =>
        new("anthropic", "claude-opus-5-5", text, null, AiStopReason.Completed, "end_turn", new AiUsage(30, 5), new AiTimings(TimeSpan.FromSeconds(1)), "hash");

    /// <summary>A clock that moves on by a fixed step every time it is read, and by hand.</summary>
    private sealed class SteppingTime(TimeSpan step) : TimeProvider
    {
        private long _now = TimeSpan.TicksPerSecond;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Interlocked.Add(ref _now, step.Ticks);

        public void Advance(TimeSpan by) => Interlocked.Add(ref _now, by.Ticks);
    }
}
