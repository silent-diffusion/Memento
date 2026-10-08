using System.Text.Json;
using Memento.AI;
using Memento.AI.Local;
using Memento.Core.Bridge;
using Memento.Core.Secrets;
using Memento.Core.Tests.Fakes;
using Memento.Generation.Tests.Support;

namespace Memento.Generation.Tests.Bridge;

/// <summary>Generation through the bridge: readiness, the privacy rule, ask-before-send, the job, the document and its record.</summary>
public sealed class GenerationBridgeTests : IDisposable
{
    private readonly M4Host _host = new();

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task ProviderReadinessFollowsSettingsKeysModelsAndVideoMemory()
    {
        static (bool Ready, string? Code, string? Reason) Of(JsonElement list, string id)
        {
            var p = list.GetProperty("providers").EnumerateArray().Single(x => x.GetProperty("id").GetString() == id);
            return (p.GetProperty("ready").GetBoolean(), p.GetProperty("code").GetString(), p.GetProperty("reason").GetString());
        }

        var initial = await _host.ResultAsync("providers.list", new { });
        Assert.False(initial.GetProperty("externalAiEnabled").GetBoolean());
        Assert.Equal((false, DomainErrorCodes.AiDisabled, "External AI is off"), Of(initial, "anthropic"));
        Assert.Equal((false, DomainErrorCodes.AiDisabled, "External AI is off"), Of(initial, "openai"));
        Assert.Equal((false, AiErrorCodes.ModelNotInstalled, "Model not installed"), Of(initial, "local"));

        _host.InstallLocalModel();
        await _host.ResultAsync("settings.set", new { ai = new { enabled = true } });
        var enabled = await _host.ResultAsync("providers.list", new { });
        Assert.Equal((false, AiErrorCodes.NoKey, "No key saved"), Of(enabled, "anthropic"));
        Assert.Equal((true, null, null), Of(enabled, "local"));
        Assert.Contains("processor", enabled.GetProperty("providers")[2].GetProperty("modelLabel").GetString(), StringComparison.Ordinal);

        await _host.Get<ISecretStore>().SetKeyAsync(AiProviders.Anthropic, "test-key-not-real", CancellationToken.None);
        var keyed = await _host.ResultAsync("providers.list", new { });
        Assert.Equal((true, null, null), Of(keyed, "anthropic"));
        Assert.Equal("claude-opus-5-5", keyed.GetProperty("providers")[0].GetProperty("modelLabel").GetString());

        // The graphics-card model needs its video memory; without it the installed processor model writes instead.
        _host.InstallLocalModel(LocalModelCatalog.Qwen35FourB);
        await _host.ResultAsync("settings.set", new { ai = new { localModelId = LocalModelCatalog.Qwen35FourB } });
        _host.Host.Probe.Snapshot = FakeResourceProbe.WithGpu(2L << 30);
        var low = await _host.ResultAsync("providers.list", new { });
        Assert.Equal((true, null, null), Of(low, "local"));
        Assert.Equal("Ministral 3 3B · processor", low.GetProperty("providers")[2].GetProperty("modelLabel").GetString());
        _host.Host.Probe.Snapshot = FakeResourceProbe.WithGpu(5L << 30);
        var fits = await _host.ResultAsync("providers.list", new { });
        Assert.Equal((true, null, null), Of(fits, "local"));
        Assert.Equal("Qwen3.5 4B · graphics card", fits.GetProperty("providers")[2].GetProperty("modelLabel").GetString());
        Assert.Equal(0, _host.Providers.CloudCreated + _host.Providers.LocalCreated);
    }

    [Fact]
    public async Task WithExternalAiOffACloudGenerationIsRefusedAndNothingIsConstructed()
    {
        var id = await _host.CreateMeetingAsync();
        await _host.Get<ISecretStore>().SetKeyAsync(AiProviders.Anthropic, "test-key-not-real", CancellationToken.None);

        var error = await _host.ErrorAsync("generation.start", new { recordingId = id, template = await _host.MeetingMinutesAsync("anthropic") });

        Assert.Equal(DomainErrorCodes.AiDisabled, error.GetProperty("code").GetString());
        Assert.Contains("nothing was sent", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal(0, _host.Providers.CloudCreated);
        Assert.Equal(0, _host.Providers.LocalCreated);
        Assert.Empty(_host.Events("generation.progress"));
        Assert.Empty((await _host.ResultAsync("documents.list", new { recordingId = id })).GetProperty("documents").EnumerateArray());
    }

    [Fact]
    public async Task ALocalGenerationWritesTheDocumentItsRecordAndAHistoryLine()
    {
        _host.InstallLocalModel();
        var id = await _host.CreateMeetingAsync();

        var start = await _host.ResultAsync("generation.start", new { recordingId = id, template = await _host.MeetingMinutesAsync() });
        Assert.False(start.GetProperty("confirmationRequired").GetBoolean());
        var done = await _host.FinishedAsync(start.GetProperty("jobId").GetString()!);

        Assert.Equal("done", done.GetProperty("stage").GetString());
        var documentId = done.GetProperty("documentId").GetString()!;
        Assert.Equal(1, _host.Providers.LocalCreated);
        Assert.Equal(0, _host.Providers.CloudCreated);
        Assert.Equal(LocalLlmDevices.Cpu, _host.Providers.Local[0].Device);
        // Exactly in this order: every event of a stage before the first of the next, the final one last.
        var raw = _host.Events("generation.progress").Select(e => e.GetProperty("stage").GetString()).ToList();
        var stages = raw.Where((s, i) => i == 0 || s != raw[i - 1]).ToList();
        Assert.Equal(["composing", "generating", "verifying", "rendering", "done"], stages);
        Assert.Contains(_host.Events("documents.changed"), e => e.GetProperty("documentId").GetString() == documentId && e.GetProperty("reason").GetString() == "generated");
        Assert.NotEmpty(_host.Events("library.changed"));

        var list = (await _host.ResultAsync("documents.list", new { recordingId = id })).GetProperty("documents");
        var summary = Assert.Single(list.EnumerateArray());
        Assert.Equal("Meeting minutes", summary.GetProperty("name").GetString());
        Assert.Equal("generated", summary.GetProperty("kind").GetString());
        Assert.Equal("local", summary.GetProperty("providerId").GetString());

        var document = (await _host.ResultAsync("documents.get", new { recordingId = id, documentId })).GetProperty("document");
        Assert.Equal("Ledgerly weekly product sync", document.GetProperty("title").GetString());
        Assert.StartsWith("Meeting minutes · Monday 5 October 2026", document.GetProperty("meta").GetString(), StringComparison.Ordinal);
        var record = document.GetProperty("record");
        Assert.True(record.GetProperty("stayedOnPc").GetBoolean());
        Assert.True(record.GetProperty("payloadKept").GetBoolean());
        Assert.Equal(64, record.GetProperty("payloadHash").GetString()!.Length);
        Assert.Contains(record.GetProperty("sent").EnumerateArray(), s => s.GetString()!.StartsWith("Transcript (", StringComparison.Ordinal));
        var minutes = record.GetProperty("modules").EnumerateArray().ToList();
        Assert.Contains(minutes, m => m.GetProperty("moduleId").GetString() == "m07" && m.GetProperty("verified").GetInt32() > 0);
        Assert.Contains(minutes, m => m.GetProperty("moduleId").GetString() == "m09" && m.GetProperty("notDiscussed").GetBoolean());

        var history = await _host.Host.Store.ReadHistoryAsync(id, CancellationToken.None);
        var line = Assert.Single(history, h => h.Summary.StartsWith("Meeting minutes generated", StringComparison.Ordinal));
        Assert.Equal("minutes", line.Stage);
        Assert.Contains("nothing was sent", line.Detail, StringComparison.Ordinal);
        Assert.Contains("Audio and video were not sent", line.Detail, StringComparison.Ordinal);
        Assert.Contains("Transcript (", line.Detail, StringComparison.Ordinal);

        var html = (await _host.ResultAsync("documents.renderHtml", new { recordingId = id, documentId, mode = "view" })).GetProperty("html").GetString()!;
        Assert.Contains("Action items", html, StringComparison.Ordinal);
        Assert.Contains("Not reached", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheLiveOutputOfAGenerationEndsBeforeItsFinalProgressAndIsNotWrittenAnywhere()
    {
        _host.InstallLocalModel();
        var id = await _host.CreateMeetingAsync();

        var jobId = (await _host.ResultAsync("generation.start", new { recordingId = id, template = await _host.MeetingMinutesAsync() })).GetProperty("jobId").GetString()!;
        await _host.FinishedAsync(jobId);

        var output = _host.Events("generation.output");
        Assert.All(output, e => Assert.Equal(jobId, e.GetProperty("jobId").GetString()));
        Assert.Equal("segment", output[0].GetProperty("step").GetString());
        Assert.Equal("done", output[^1].GetProperty("kind").GetString());
        Assert.Single(output, e => e.GetProperty("kind").GetString() == "done");
        var requests = output.Where(e => e.GetProperty("kind").GetString() == "request").ToList();
        Assert.NotEmpty(requests);
        Assert.All(requests, r => Assert.StartsWith("System\n", r.GetProperty("text").GetString(), StringComparison.Ordinal));
        Assert.All(requests, r => Assert.Single(output, e => e.GetProperty("kind").GetString() == "reply" && e.GetProperty("passId").GetString() == r.GetProperty("passId").GetString()));

        // done is posted before the job's final generation.progress.
        var names = _host.Sink.Posted.Select(p => System.Text.Json.JsonDocument.Parse(p).RootElement).Select(e => (Name: e.GetProperty("event").GetString(), Kind: e.GetProperty("payload").TryGetProperty("kind", out var k) ? k.GetString() : e.GetProperty("payload").TryGetProperty("stage", out var s) ? s.GetString() : null)).ToList();
        Assert.True(names.IndexOf(("generation.output", "done")) < names.IndexOf(("generation.progress", "done")));

        // The exchange stays in memory: no file in the library holds a request or a reply.
        var sample = requests[0].GetProperty("text").GetString()!.Split('\n')[1];
        foreach (var file in Directory.EnumerateFiles(_host.Directory.Path, "*", SearchOption.AllDirectories).Where(f => !f.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)))
        {
            Assert.DoesNotContain(sample, await File.ReadAllTextAsync(file), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task AskBeforeEverySendHoldsACloudJobUntilConfirmed()
    {
        var id = await _host.CreateMeetingAsync();
        await _host.ResultAsync("settings.set", new { ai = new { enabled = true } });
        await _host.Get<ISecretStore>().SetKeyAsync(AiProviders.Anthropic, "test-key-not-real", CancellationToken.None);
        var template = await _host.MeetingMinutesAsync("anthropic");

        var held = await _host.ResultAsync("generation.start", new { recordingId = id, template });
        Assert.True(held.GetProperty("confirmationRequired").GetBoolean());
        var summary = held.GetProperty("summary");
        Assert.Equal("Claude", summary.GetProperty("providerName").GetString());
        Assert.False(summary.GetProperty("inputsUsed").GetProperty("attachments").GetBoolean());
        Assert.True(summary.GetProperty("bytes").GetInt64() > 1000);
        Assert.Equal(0, _host.Providers.CloudCreated);
        Assert.Equal(DomainErrorCodes.GenerationBusy, (await _host.ErrorAsync("generation.start", new { recordingId = id, template })).GetProperty("code").GetString());

        await _host.ResultAsync("generation.confirm", new { jobId = held.GetProperty("jobId").GetString(), approved = false });
        Assert.Equal("cancelled", (await _host.FinishedAsync(held.GetProperty("jobId").GetString()!)).GetProperty("stage").GetString());
        Assert.Equal(0, _host.Providers.CloudCreated);

        var again = await _host.ResultAsync("generation.start", new { recordingId = id, template });
        await _host.ResultAsync("generation.confirm", new { jobId = again.GetProperty("jobId").GetString(), approved = true });
        var done = await _host.FinishedAsync(again.GetProperty("jobId").GetString()!);
        Assert.Equal("done", done.GetProperty("stage").GetString());
        Assert.Equal([("anthropic", "claude-opus-5-5")], _host.Providers.Cloud);
        var record = (await _host.ResultAsync("documents.get", new { recordingId = id, documentId = done.GetProperty("documentId").GetString() })).GetProperty("document").GetProperty("record");
        Assert.False(record.GetProperty("stayedOnPc").GetBoolean());
    }

    [Fact]
    public async Task TheShareSwitchesLimitWhatACloudProviderReceives()
    {
        var id = await _host.CreateMeetingAsync();
        await _host.ResultAsync("settings.set", new { ai = new { enabled = true, share = new { agenda = false, participants = false } } });
        await _host.Get<ISecretStore>().SetKeyAsync(AiProviders.OpenAi, "test-key-not-real", CancellationToken.None);

        var cloud = await _host.ResultAsync("generation.preview", new { recordingId = id, template = await _host.MeetingMinutesAsync("openai") });
        var local = await _host.ResultAsync("generation.preview", new { recordingId = id, template = await _host.MeetingMinutesAsync("local") });

        Assert.False(cloud.GetProperty("inputsUsed").GetProperty("agenda").GetBoolean());
        Assert.DoesNotContain("<agenda>", cloud.GetProperty("payloadText").GetString(), StringComparison.Ordinal);
        Assert.Contains("Exactly what will be sent to ChatGPT", cloud.GetProperty("payloadText").GetString(), StringComparison.Ordinal);
        Assert.Contains("Audio and video are never sent.", cloud.GetProperty("payloadText").GetString(), StringComparison.Ordinal);
        Assert.True(local.GetProperty("inputsUsed").GetProperty("agenda").GetBoolean());
        Assert.True(local.GetProperty("staysOnPc").GetBoolean());
        Assert.Contains("nothing leaves this PC", local.GetProperty("payloadText").GetString(), StringComparison.Ordinal);
        Assert.True(cloud.GetProperty("chunks").GetInt32() >= 1);
        Assert.Equal(0, _host.Providers.CloudCreated + _host.Providers.LocalCreated);
    }

    [Fact]
    public async Task AFailureKeepsTheProvidersCopyAndLeavesTheDocumentAlone()
    {
        _host.InstallLocalModel();
        var id = await _host.CreateMeetingAsync();
        var first = await _host.FinishedAsync((await _host.ResultAsync("generation.start", new { recordingId = id, template = await _host.MeetingMinutesAsync() })).GetProperty("jobId").GetString()!);
        var documentId = first.GetProperty("documentId").GetString()!;
        var before = (await _host.ResultAsync("documents.get", new { recordingId = id, documentId })).GetProperty("document").GetRawText();

        _host.Providers.Make = _ => new FailingProvider(AiErrors.WorkerCrashed(LocalAiProvider.ProviderName, "Ministral 3 3B"));
        var failed = await _host.FinishedAsync((await _host.ResultAsync("generation.start", new { recordingId = id, template = await _host.MeetingMinutesAsync(), documentId })).GetProperty("jobId").GetString()!);

        Assert.Equal("failed", failed.GetProperty("stage").GetString());
        Assert.Equal(AiErrorCodes.WorkerCrashed, failed.GetProperty("code").GetString());
        Assert.Contains("Nothing left this PC and no document was changed.", failed.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal(before, (await _host.ResultAsync("documents.get", new { recordingId = id, documentId })).GetProperty("document").GetRawText());
        Assert.Contains(await _host.Host.Store.ReadHistoryAsync(id, CancellationToken.None), h => h.Event == "failed" && h.Stage == "minutes");
    }

    [Fact]
    public async Task CancellingStopsTheJobAndWritesNothing()
    {
        _host.InstallLocalModel();
        var id = await _host.CreateMeetingAsync();
        var blocking = new BlockingProvider();
        _host.Providers.Make = _ => blocking;

        var start = await _host.ResultAsync("generation.start", new { recordingId = id, template = await _host.MeetingMinutesAsync() });
        await blocking.Started.Task.WaitAsync(Patience.Ceiling);
        await _host.ResultAsync("generation.cancel", new { jobId = start.GetProperty("jobId").GetString() });

        Assert.Equal("cancelled", (await _host.FinishedAsync(start.GetProperty("jobId").GetString()!)).GetProperty("stage").GetString());
        Assert.Empty((await _host.ResultAsync("documents.list", new { recordingId = id })).GetProperty("documents").EnumerateArray());
        Assert.Equal(DomainErrorCodes.GenerationNotFound, (await _host.ErrorAsync("generation.cancel", new { jobId = "g000000000000" })).GetProperty("code").GetString());
    }

    [Fact]
    public async Task RefusalsAreSpecific()
    {
        var id = await _host.CreateMeetingAsync();
        var template = await _host.MeetingMinutesAsync();

        var notInstalled = await _host.ErrorAsync("generation.start", new { recordingId = id, template });
        Assert.Equal(DomainErrorCodes.AiProviderNotReady, notInstalled.GetProperty("code").GetString());
        Assert.Equal(AiErrorCodes.ModelNotInstalled, notInstalled.GetProperty("detail").GetString());

        _host.InstallLocalModel();
        var bare = await _host.Host.Store.CreateAsync(new Core.Projects.ProjectCreateRequest("Empty", "meeting", DateTimeOffset.Now, Core.Projects.ProjectStates.Ready), CancellationToken.None);
        Assert.Equal(DomainErrorCodes.GenerationNoTranscript, (await _host.ErrorAsync("generation.start", new { recordingId = bare.Id, template })).GetProperty("code").GetString());
        Assert.Equal(DomainErrorCodes.ProjectNotFound, (await _host.ErrorAsync("generation.start", new { recordingId = "20260101-000000-aaaaaa", template })).GetProperty("code").GetString());
        Assert.Equal(DomainErrorCodes.DocumentsNotFound, (await _host.ErrorAsync("generation.start", new { recordingId = id, template, documentId = "dmissing" })).GetProperty("code").GetString());
        Assert.Equal(BridgeErrorCodes.InvalidParams, (await _host.Host.CallAsync("generation.start", $$"""{"recordingId":"{{id}}"}""")).GetProperty("error").GetProperty("code").GetString());
    }

    private sealed class FailingProvider(AiError error) : IAiProvider
    {
        private readonly MeetingProvider _shape = new();

        public string Id => _shape.Id;

        public string DisplayName => _shape.DisplayName;

        public AiProviderKind Kind => _shape.Kind;

        public string Model => _shape.Model;

        public AiCapabilities Capabilities => _shape.Capabilities;

        public int CountTokens(string text) => _shape.CountTokens(text);

        public Task<AiReadiness> CheckAsync(CancellationToken cancellationToken) => Task.FromResult(AiReadiness.Ready());

        public Task<AiResponse> GenerateAsync(AiRequest request, IProgress<AiProgress>? progress, CancellationToken cancellationToken) =>
            throw new AiException(error);
    }

    private sealed class BlockingProvider : IAiProvider
    {
        private readonly MeetingProvider _shape = new();

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Id => _shape.Id;

        public string DisplayName => _shape.DisplayName;

        public AiProviderKind Kind => _shape.Kind;

        public string Model => _shape.Model;

        public AiCapabilities Capabilities => _shape.Capabilities;

        public int CountTokens(string text) => _shape.CountTokens(text);

        public Task<AiReadiness> CheckAsync(CancellationToken cancellationToken) => Task.FromResult(AiReadiness.Ready());

        public async Task<AiResponse> GenerateAsync(AiRequest request, IProgress<AiProgress>? progress, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }
    }
}
