using Memento.AI.Anthropic;
using Memento.AI.OpenAI;
using Memento.AI.Tests.Fakes;
using Memento.Core.Ai;

namespace Memento.AI.Tests.Cloud;

/// <summary>
/// The model chosen in Settings › AI and privacy reaches the fake server exactly as chosen, with only the settings that
/// model takes, and a model the service does not know is reported as such: never retried, never replaced by another.
/// </summary>
public sealed class CloudModelRequestTests : IDisposable
{
    private const string AnthropicKey = "test-only-anthropic-key-5e1b7d3a90";
    private const string OpenAiKey = "test-only-openai-key-8c2f6a1e47";

    private readonly ScriptedHttpServer _server = new();
    private readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly InstantTimeProvider _time = new();
    private readonly FakeSecrets _secrets = new FakeSecrets().With("anthropic", AnthropicKey).With("openai", OpenAiKey);

    public void Dispose()
    {
        _http.Dispose();
        _server.Dispose();
    }

    [Theory]
    [InlineData("claude-opus-5-5", true, true)]
    [InlineData("claude-fable-5-1", true, true)]
    [InlineData("claude-sonnet-5-5", true, true)]
    [InlineData("claude-haiku-5-5", true, false)]
    [InlineData("claude-opus-4-8", false, false)]
    public async Task ClaudeIsSentTheChosenModelWithTheSettingsItTakes(string model, bool effort, bool fallback)
    {
        _server.Enqueue(AnthropicStreams.Text(["ok"], model: model));

        var response = await Claude(model).GenerateAsync(AiRequest.Create("test.model", "Be brief.", "Hi") with { MaxOutputTokens = 8000 }, null, CancellationToken.None);

        var sent = Assert.Single(_server.Requests);
        var body = sent.Json;
        Assert.Equal(model, body.GetProperty("model").GetString());
        Assert.Equal(model, response.Model);
        Assert.Equal(effort, body.TryGetProperty("output_config", out var config) && config.TryGetProperty("effort", out _));
        Assert.Equal(fallback, body.TryGetProperty("fallbacks", out _));
        Assert.Equal(fallback ? "server-side-fallback-2026-07-01" : null, sent.Header("anthropic-beta"));
        Assert.Equal(model == "claude-opus-4-8" ? 24_000 : 8000 + 16_000, body.GetProperty("max_tokens").GetInt32());
    }

    [Fact]
    public async Task AModelTypedByHandGetsAConservativeAnswerLimit()
    {
        _server.Enqueue(AnthropicStreams.Text(["ok"], model: "claude-opus-4-1"));

        var provider = Claude("claude-opus-4-1");
        await provider.GenerateAsync(AiRequest.Create("test.model", string.Empty, "Hi") with { MaxOutputTokens = 30_000 }, null, CancellationToken.None);

        Assert.Equal(AiModelDefaults.UnlistedAnthropicOutputTokens, Assert.Single(_server.Requests).Json.GetProperty("max_tokens").GetInt32());
        Assert.Equal(AiModelDefaults.UnlistedAnthropicContextTokens, provider.Capabilities.MaxContextTokens);
    }

    [Theory]
    [InlineData("gpt-6-astra", true)]
    [InlineData("gpt-6.1-sol", true)]
    [InlineData("gpt-6-luna", true)]
    [InlineData("gpt-4o", false)]
    public async Task ChatGptIsSentTheChosenModelWithTheSettingsItTakes(string model, bool effort)
    {
        _server.Enqueue(OpenAiStreams.Text(["ok"], model: model));

        var response = await ChatGpt(model).GenerateAsync(AiRequest.Create("test.model", "Be brief.", "Hi") with { MaxOutputTokens = 8000 }, null, CancellationToken.None);

        var body = Assert.Single(_server.Requests).Json;
        Assert.Equal(model, body.GetProperty("model").GetString());
        Assert.Equal(model, response.Model);
        Assert.Equal(effort, body.TryGetProperty("reasoning", out _));
        Assert.Equal(effort ? 24_000 : AiModelDefaults.UnlistedOpenAiOutputTokens, body.GetProperty("max_output_tokens").GetInt32());
    }

    [Theory]
    [InlineData(404, "not_found_error", "model: claude-nonexistent-9")]
    [InlineData(400, "invalid_request_error", "The model 'claude-nonexistent-9' does not exist.")]
    public async Task ClaudeNotOfferingTheModelIsItsOwnError(int status, string type, string message)
    {
        _server.Enqueue(AnthropicStreams.Error(status, type, message));

        var error = await Assert.ThrowsAsync<AiException>(() => Claude("claude-nonexistent-9").GenerateAsync(AiRequest.Create("test.model", string.Empty, "Hi"), null, CancellationToken.None));

        Assert.Equal(AiErrorCodes.UnknownModel, error.Error.Code);
        Assert.Equal(
            "Claude does not offer a model called 'claude-nonexistent-9'. Nothing was generated and no document was changed. Choose one from the list in Settings › AI and privacy, or check the id.",
            error.Error.Message);
        Assert.Equal(status, error.Error.HttpStatus);
        Assert.Equal("claude-nonexistent-9", Assert.Single(_server.Requests).Json.GetProperty("model").GetString());
    }

    [Theory]
    [InlineData(404, "invalid_request_error", "model_not_found", "The model `gpt-9-nova` does not exist or you do not have access to it.")]
    [InlineData(400, "invalid_request_error", null, "Invalid model: gpt-9-nova does not exist.")]
    public async Task ChatGptNotOfferingTheModelIsItsOwnError(int status, string type, string? code, string message)
    {
        _server.Enqueue(OpenAiStreams.Error(status, type, code, message));

        var error = await Assert.ThrowsAsync<AiException>(() => ChatGpt("gpt-9-nova").GenerateAsync(AiRequest.Create("test.model", string.Empty, "Hi"), null, CancellationToken.None));

        Assert.Equal(AiErrorCodes.UnknownModel, error.Error.Code);
        Assert.StartsWith("ChatGPT does not offer a model called 'gpt-9-nova'.", error.Error.Message, StringComparison.Ordinal);
        Assert.Single(_server.Requests);
    }

    [Theory]
    [InlineData(400, "invalid_request_error", "max_tokens: must be at most 64000")]
    [InlineData(400, "invalid_request_error", "output_config.effort is not supported for claude-haiku-5-5")]
    [InlineData(404, "not_found_error", "Not found")]
    public async Task OtherRejectionsAreNotTakenForAnUnknownModel(int status, string type, string message)
    {
        _server.Enqueue(AnthropicStreams.Error(status, type, message));

        var error = await Assert.ThrowsAsync<AiException>(() => Claude("claude-haiku-5-5").GenerateAsync(AiRequest.Create("test.model", string.Empty, "Hi"), null, CancellationToken.None));

        Assert.Equal(AiErrorCodes.ProviderError, error.Error.Code);
    }

    [Fact]
    public async Task AnswerWithoutAJsonErrorIsNotTakenForAnUnknownModel()
    {
        _server.Enqueue(ScriptedResponse.Json(404, "{}"));

        var error = await Assert.ThrowsAsync<AiException>(() => ChatGpt("gpt-6-luna").GenerateAsync(AiRequest.Create("test.model", string.Empty, "Hi"), null, CancellationToken.None));

        Assert.Equal(AiErrorCodes.ProviderError, error.Error.Code);
    }

    private AnthropicProvider Claude(string model) =>
        new(_http, _secrets, AnthropicOptions.For(AiModelDefaults.Describe("anthropic", model), _server.BaseUrl), new SpyLogger<AnthropicProvider>(), _time);

    private OpenAiProvider ChatGpt(string model) =>
        new(_http, _secrets, OpenAiOptions.For(AiModelDefaults.Describe("openai", model), _server.BaseUrl), new SpyLogger<OpenAiProvider>(), _time);
}
