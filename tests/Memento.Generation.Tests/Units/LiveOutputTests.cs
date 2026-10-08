using System.Collections.Concurrent;
using Memento.AI;
using Memento.AI.Local;
using Memento.AI.Payload;
using Memento.Core.Bridge.Contracts;
using Memento.Documents.Model.Modules;
using Memento.Documents.Templates;
using Memento.Generation.Documents;
using Memento.Generation.Generation;
using Memento.Generation.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace Memento.Generation.Tests.Units;

/// <summary>
/// What the Live output sheet receives from a generation: the local model's requests as it starts reading each one, its
/// tokens and replies as they complete (before the batch ends), a cloud provider's requests and whole replies, and the
/// steps done in code, in pipeline order.
/// </summary>
public sealed class LiveOutputTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("memento-live-output-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public async Task TheLocalModelsRequestsTokensAndRepliesArriveInOrderWithTheirTitles()
    {
        var engine = new ScriptedLocalEngine { Answer = p => "{\"items\": [\"" + string.Join(' ', Enumerable.Range(1, 30).Select(i => p.Purpose + i)) + "\"]}" };
        var provider = Local(engine);
        var delivered = new ConcurrentQueue<GenerationOutput>();
        var feed = new GenerationOutputFeed("g1", delivered.Enqueue, new FrozenTime());
        var requests = new[] { AiRequest.Create("map.a", "Extract.", "[1] Ana: one.", 200) with { Grammar = "root ::= \"x\"" }, AiRequest.Create("map.b", "Extract.", "[2] Bo: two.", 200) };
        var finished = new List<int>();

        IReadOnlyList<AiResponse> responses;
        await using (var runner = new RequestRunner(provider, output: feed))
        {
            responses = await runner.RunAsync(requests, new InlineProgress<int>(finished.Add), CancellationToken.None, [new OutputPass("map", "Decisions and action items · segment 1 of 2"), new OutputPass("map", "Decisions and action items · segment 2 of 2")]);
        }

        await feed.CompleteAsync();
        var events = delivered.ToList();
        // The feed sends the first token at once and holds the rest until the reply, while time stands still.
        Assert.Equal(["request", "token", "token", "reply", "request", "token", "reply", "done"], events.Select(e => e.Kind));
        Assert.Equal(["p1", "p1", "p1", "p1", "p2", "p2", "p2", string.Empty], events.Select(e => e.PassId));
        Assert.Equal("Decisions and action items · segment 2 of 2", events[4].Title);
        Assert.All(events.Where(e => e.Kind == "request"), e => Assert.True(e.Streamed));
        for (var i = 0; i < 2; i++)
        {
            var pass = "p" + (i + 1);
            Assert.Equal(AiRequestText.Render(requests[i]), events.Single(e => e.PassId == pass && e.Kind == "request").Text);
            Assert.Equal(responses[i].Text, string.Concat(events.Where(e => e.PassId == pass && e.Kind == "token").Select(e => e.Text)));
            var reply = events.Single(e => e.PassId == pass && e.Kind == "reply");
            Assert.Equal((responses[i].Text, responses[i].Usage.OutputTokens, "eog"), (reply.Text, reply.OutputTokens, reply.StopReason));
            Assert.Equal(Math.Round((responses[i].Usage.OutputTokens - 1) / 0.04, 1), reply.TokensPerSecond);
        }

        Assert.Equal([0, 1], finished);
    }

    [Fact]
    public async Task APipelineRunShowsTheStepsInCodeAndEveryExchangeInPipelineOrder()
    {
        var provider = new MeetingProvider();
        var delivered = new ConcurrentQueue<GenerationOutput>();
        var feed = new GenerationOutputFeed("g1", delivered.Enqueue, new FrozenTime());
        var material = SyntheticMeeting.Material();
        var payload = PayloadComposer.Compose(material.ToPayloadInputs(null), SyntheticMeeting.AllInputs);
        var facts = new GenerationFacts(material.Details.Title, null, DataModuleComposer.Participants(material), material.Details.Agenda.Items, []);
        var input = new PipelineInput(BuiltInTemplates.MeetingMinutes, material, payload, SyntheticMeeting.AllInputs, provider, facts, 3000, 1400, Bounded: true, GenerationPipeline.LocalVerifyBatchSize) { Output = feed };

        await new GenerationPipeline(ModuleCatalog.Default).RunAsync(input, null, CancellationToken.None);
        await feed.CompleteAsync();

        var events = delivered.ToList();
        var steps = events.Where(e => e.Kind is "step" or "request").Select(e => e.Step).ToList();
        var order = steps.Where((s, i) => i == 0 || s != steps[i - 1]).ToList();
        Assert.Equal(["segment", "map", "reduce", "verify", "grounding"], order);
        Assert.Matches(@"^\d+ segments? of up to 3,000 tokens, cut at chapters and speaker turns: \d+:\d\d–\d+:\d\d", events[0].Text);
        Assert.Contains(events, e => e.Kind == "request" && e.Title!.StartsWith("Decisions and action items · segment 1 of ", StringComparison.Ordinal));
        Assert.Contains(events, e => e.Kind == "request" && e.Title!.StartsWith("Agenda · segment 1 of ", StringComparison.Ordinal));
        Assert.Contains(events, e => e.Kind == "request" && e.Title!.StartsWith("Check Decisions and action items · ", StringComparison.Ordinal));
        Assert.Matches(@"^\d+ of \d+ claims kept: ", events.Single(e => e.Step == "grounding").Text);
        var requests = events.Where(e => e.Kind == "request").ToList();
        Assert.Equal(provider.Requests.Count, requests.Count);
        Assert.Equal(provider.Requests.Select(AiRequestText.Render).Order(StringComparer.Ordinal), requests.Select(e => e.Text).Order(StringComparer.Ordinal));
        Assert.All(requests, r => Assert.Single(events, e => e.Kind == "reply" && e.PassId == r.PassId));
        Assert.Equal(events.Select(e => e.PassId).Where(p => p.Length > 0).Distinct().Count(), events.Count(e => e.Kind is "step" or "request"));
        Assert.Equal("done", events[^1].Kind);
    }

    private LocalAiProvider Local(ScriptedLocalEngine engine)
    {
        var path = Path.Combine(_folder, "model.gguf");
        File.WriteAllBytes(path, [1, 2, 3, 4]);
        var model = LocalModelCatalog.Find(LocalModelCatalog.Ministral3ThreeB)! with { SizeBytes = 4 };
        var runner = new LocalLlmJobRunner(engine, NullLogger<LocalLlmJobRunner>.Instance, new FrozenTime());
        return new LocalAiProvider(model, path, new InProcessLocalLlmJobClient(runner), EstimatingTokenCounter.Generic, () => null);
    }

    /// <summary>Time stands still: the worker sends the first piece and every eighth; the feed only the first and what waits at a reply.</summary>
    private sealed class FrozenTime : TimeProvider
    {
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => TimeSpan.TicksPerSecond;
    }
}
