using System.Text.Json;
using Memento.AI;
using Memento.AI.Anthropic;
using Memento.AI.OpenAI;
using Memento.Core.Ai;
using Memento.Core.Secrets;
using Memento.Generation.Ai;
using Memento.Generation.Tests.Support;

namespace Memento.Generation.Tests.Bridge;

/// <summary>
/// The model chosen in Settings › AI and privacy is the one sent, and every place that names the request names it:
/// providers.list, the preview, the send summary, the document's record and History. A model the service does not know
/// fails with its own code and copy, and nothing else is tried.
/// </summary>
public sealed class CloudModelChoiceTests : IDisposable
{
    private readonly M4Host _host = new();

    public void Dispose() => _host.Dispose();

    private async Task ClaudeReadyWithAsync(string model, bool askBeforeSend = false)
    {
        await _host.ResultAsync("settings.set", new { ai = new { enabled = true, askBeforeSend, providers = new { anthropic = new { model } } } });
        await _host.Get<ISecretStore>().SetKeyAsync(AiProviders.Anthropic, "test-key-not-real", CancellationToken.None);
    }

    private async Task<JsonElement> ProviderAsync(string id) =>
        (await _host.ResultAsync("providers.list", new { })).GetProperty("providers").EnumerateArray().Single(p => p.GetProperty("id").GetString() == id);

    [Fact]
    public async Task ProvidersListNamesTheChosenModelAndWhatItReads()
    {
        await ClaudeReadyWithAsync("claude-haiku-5-5");
        await _host.ResultAsync("settings.set", new { ai = new { providers = new { openai = new { model = "gpt-6-luna" } } } });

        var claude = await ProviderAsync("anthropic");
        var chatGpt = await ProviderAsync("openai");
        var local = await ProviderAsync("local");

        Assert.True(claude.GetProperty("ready").GetBoolean());
        Assert.Equal("claude-haiku-5-5", claude.GetProperty("modelLabel").GetString());
        Assert.True(claude.GetProperty("modelListed").GetBoolean());
        Assert.Equal(
            "Sends the ticked inputs to Claude (claude-haiku-5-5) when you press Generate. claude-haiku-5-5 reads up to 1M tokens a request; a long transcript is split into segments of up to 24,000 tokens, so it always fits.",
            claude.GetProperty("detail").GetString());
        Assert.Equal("gpt-6-luna", chatGpt.GetProperty("modelLabel").GetString());
        Assert.True(chatGpt.GetProperty("modelListed").GetBoolean());
        Assert.Equal(JsonValueKind.Null, local.GetProperty("modelListed").ValueKind);
    }

    [Fact]
    public async Task AModelTypedByHandIsUsedAsTypedAndSaidToBeUnlisted()
    {
        await ClaudeReadyWithAsync("claude-opus-4-8");

        var claude = await ProviderAsync("anthropic");

        Assert.Equal("claude-opus-4-8", claude.GetProperty("modelLabel").GetString());
        Assert.False(claude.GetProperty("modelListed").GetBoolean());
        Assert.Contains("claude-opus-4-8 is not in Memento's list, so Memento assumes it reads up to 200k tokens a request and sends it no effort setting", claude.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Equal("claude-opus-4-8", _host.Host.Settings.Current.Ai.Providers.Anthropic.Model);
    }

    [Fact]
    public async Task ThePreviewAndTheSendSummaryNameTheChosenModel()
    {
        var id = await _host.CreateMeetingAsync();
        await ClaudeReadyWithAsync("claude-sonnet-5-5", askBeforeSend: true);
        var template = await _host.MeetingMinutesAsync("anthropic");

        var preview = await _host.ResultAsync("generation.preview", new { recordingId = id, template });
        var held = await _host.ResultAsync("generation.start", new { recordingId = id, template });

        Assert.Equal("claude-sonnet-5-5", preview.GetProperty("provider").GetProperty("modelLabel").GetString());
        Assert.Contains("Claude (claude-sonnet-5-5)", preview.GetProperty("payloadText").GetString(), StringComparison.Ordinal);
        Assert.Equal("claude-sonnet-5-5", held.GetProperty("summary").GetProperty("modelLabel").GetString());
        Assert.Equal(0, _host.Providers.CloudCreated);
        await _host.ResultAsync("generation.confirm", new { jobId = held.GetProperty("jobId").GetString(), approved = false });
    }

    [Fact]
    public async Task TheRecordAndHistoryNameTheModelThatWasSentTo()
    {
        var id = await _host.CreateMeetingAsync();
        await ClaudeReadyWithAsync("claude-haiku-5-5");

        var started = await _host.ResultAsync("generation.start", new { recordingId = id, template = await _host.MeetingMinutesAsync("anthropic") });
        var done = await _host.FinishedAsync(started.GetProperty("jobId").GetString()!);

        Assert.Equal("done", done.GetProperty("stage").GetString());
        Assert.Equal([("anthropic", "claude-haiku-5-5")], _host.Providers.Cloud);
        var record = (await _host.ResultAsync("documents.get", new { recordingId = id, documentId = done.GetProperty("documentId").GetString() })).GetProperty("document").GetProperty("record");
        Assert.Equal("claude-haiku-5-5", record.GetProperty("modelLabel").GetString());
        var history = await _host.Host.Store.ReadHistoryAsync(id, CancellationToken.None);
        var line = Assert.Single(history, h => h.Summary.StartsWith("Meeting minutes generated", StringComparison.Ordinal));
        Assert.Contains("Claude (claude-haiku-5-5)", line.Summary, StringComparison.Ordinal);
        Assert.Contains("sent to Claude (claude-haiku-5-5)", line.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnknownModelFailsWithItsOwnCodeAndNothingElseIsTried()
    {
        var id = await _host.CreateMeetingAsync();
        await ClaudeReadyWithAsync("claude-nonexistent-9");
        _host.Providers.Make = _ => new UnknownModelProvider(AiErrors.UnknownModel("Claude", "claude-nonexistent-9", 404, "not_found_error"));

        var started = await _host.ResultAsync("generation.start", new { recordingId = id, template = await _host.MeetingMinutesAsync("anthropic") });
        var failed = await _host.FinishedAsync(started.GetProperty("jobId").GetString()!);

        Assert.Equal("failed", failed.GetProperty("stage").GetString());
        Assert.Equal(AiErrorCodes.UnknownModel, failed.GetProperty("code").GetString());
        Assert.Equal(
            "Claude does not offer a model called 'claude-nonexistent-9'. Nothing was generated and no document was changed. Choose one from the list in Settings › AI and privacy, or check the id.",
            failed.GetProperty("message").GetString());
        Assert.Equal([("anthropic", "claude-nonexistent-9")], _host.Providers.Cloud);
        Assert.Contains(await _host.Host.Store.ReadHistoryAsync(id, CancellationToken.None), h => h.Event == "failed" && h.Stage == "minutes");
    }

    [Theory]
    [InlineData("claude-opus-5-5", "high", true, 1_000_000)]
    [InlineData("claude-fable-5-1", "high", true, 1_000_000)]
    [InlineData("claude-sonnet-5-5", "high", true, 1_000_000)]
    [InlineData("claude-haiku-5-5", "high", false, 1_000_000)]
    [InlineData("claude-opus-4-8", null, false, 200_000)]
    public void TheClaudeOptionsFollowTheModel(string model, string? effort, bool fallback, int context)
    {
        var options = AiProviderFactory.AnthropicOptionsFor(model);

        Assert.Equal(model, options.Model);
        Assert.Equal(effort, options.Effort);
        Assert.Equal(fallback, options.UseServerSideFallback);
        Assert.Equal(context, options.ContextTokens);
        Assert.Equal(AnthropicOptions.DefaultBaseUrl, options.BaseUrl);
    }

    [Theory]
    [InlineData("gpt-6-astra", "high", 1_050_000)]
    [InlineData("gpt-6.1-sol", "high", 1_050_000)]
    [InlineData("gpt-6-luna", "high", 1_050_000)]
    [InlineData("gpt-4o", null, 128_000)]
    public void TheOpenAiOptionsFollowTheModel(string model, string? effort, int context)
    {
        var options = AiProviderFactory.OpenAiOptionsFor(model);

        Assert.Equal(model, options.Model);
        Assert.Equal(effort, options.ReasoningEffort);
        Assert.Equal(context, options.ContextTokens);
        Assert.Equal(OpenAiOptions.DefaultBaseUrl, options.BaseUrl);
    }

    [Fact]
    public void TheChunksFollowTheChosenModelsContext()
    {
        static ProviderStatus Cloud(string model) => new("anthropic", true, null, null, null, model, model) { Cloud = AiModelDefaults.Describe("anthropic", model) };

        Assert.Equal(24_000, Cloud("claude-haiku-5-5").ChunkTokens);
        Assert.Equal(24_000, Cloud("claude-opus-4-8").ChunkTokens);
        Assert.Equal(24_000, new ProviderStatus("openai", true, null, null, null, "gpt-4o", "gpt-4o") { Cloud = AiModelDefaults.Describe("openai", "gpt-4o") }.ChunkTokens);
        Assert.Equal(16_000, new ProviderStatus("anthropic", true, null, null, null, "x", "x") { Cloud = new CloudModel("x", "n", 64_000, 8000) }.ChunkTokens);
    }

    private sealed class UnknownModelProvider(AiError error) : IAiProvider
    {
        private readonly MeetingProvider _shape = new(AiProviderKind.Cloud, 1_000_000);

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
}
