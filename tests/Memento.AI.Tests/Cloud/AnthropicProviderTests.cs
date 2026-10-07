using System.Text.Json;
using Memento.AI.Anthropic;
using Memento.AI.Http;
using Memento.AI.Tests.Fakes;

namespace Memento.AI.Tests.Cloud;

public sealed class AnthropicProviderTests : IDisposable
{
    private const string Key = "test-only-anthropic-key-7f3a9c2e1b";

    private static readonly JsonElement Schema = JsonDocument.Parse("""
        {"type":"object","properties":{"answer":{"type":"string"}},"required":["answer"],"additionalProperties":false}
        """).RootElement.Clone();

    private readonly ScriptedHttpServer _server = new();
    private readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly SpyLogger<AnthropicProvider> _log = new();
    private readonly InstantTimeProvider _time = new();
    private readonly FakeSecrets _secrets = new FakeSecrets().With("anthropic", Key);

    public void Dispose()
    {
        _http.Dispose();
        _server.Dispose();
    }

    [Fact]
    public async Task StreamsTextAndReportsUsageModelAndHash()
    {
        _server.Enqueue(AnthropicStreams.Text(["Hello", ", ", "world."], input: 42, output: 7, withThinking: true));
        var progress = new ProgressLog<AiProgress>();
        var request = AiRequest.Create("test.plain", "Be brief.", "Say hello.");

        var response = await Provider().GenerateAsync(request, progress, CancellationToken.None);

        Assert.Equal("Hello, world.", response.Text);
        Assert.Equal(AiStopReason.Completed, response.StopReason);
        Assert.Equal("end_turn", response.ProviderStopReason);
        Assert.Equal(new AiUsage(42, 7, 0), response.Usage);
        Assert.Equal("claude-opus-5-5", response.Model);
        Assert.Null(response.Json);
        Assert.False(response.FellBack);
        Assert.Equal(1, response.Timings.Attempts);
        Assert.NotNull(response.Timings.FirstToken);
        Assert.Equal(AiRequestHash.Compute(request, "anthropic", "claude-opus-5-5"), response.RequestHash);
        Assert.Equal(["Hello", ", ", "world."], progress.Items.Where(p => p.Stage == AiProgressStage.Generating).Select(p => p.Delta!));
        Assert.Equal(AiProgressStage.Done, progress.Items[^1].Stage);
    }

    [Fact]
    public async Task SendsTheMessagesApiShapeWithStructuredOutputAndFallback()
    {
        _server.Enqueue(AnthropicStreams.Text(["{\"answer\":", "\"42\"}"]));
        var request = new AiRequest
        {
            Purpose = "test.json",
            System = "Answer in JSON.",
            Messages = [AiMessage.User("What is six times seven?"), AiMessage.Assistant("Let me check."), AiMessage.User("Go on.")],
            MaxOutputTokens = 2000,
            Temperature = 0,
            JsonSchema = Schema,
        };

        var response = await Provider().GenerateAsync(request, null, CancellationToken.None);

        Assert.Equal("42", response.Json!.Value.GetProperty("answer").GetString());
        var sent = Assert.Single(_server.Requests);
        Assert.Equal("POST", sent.Method);
        Assert.Equal("/v1/messages", sent.Path);
        Assert.Equal(Key, sent.Header("x-api-key"));
        Assert.Equal("2023-06-01", sent.Header("anthropic-version"));
        Assert.Equal("server-side-fallback-2026-07-01", sent.Header("anthropic-beta"));
        var body = sent.Json;
        Assert.Equal("claude-opus-5-5", body.GetProperty("model").GetString());
        Assert.True(body.GetProperty("stream").GetBoolean());
        Assert.Equal(2000 + 16_000, body.GetProperty("max_tokens").GetInt32());
        Assert.Equal("Answer in JSON.", body.GetProperty("system").GetString());
        Assert.Equal(["user", "assistant", "user"], body.GetProperty("messages").EnumerateArray().Select(m => m.GetProperty("role").GetString()!));
        Assert.Equal("high", body.GetProperty("output_config").GetProperty("effort").GetString());
        Assert.Equal("json_schema", body.GetProperty("output_config").GetProperty("format").GetProperty("type").GetString());
        Assert.Equal("string", body.GetProperty("output_config").GetProperty("format").GetProperty("schema").GetProperty("properties").GetProperty("answer").GetProperty("type").GetString());
        Assert.Equal("default", body.GetProperty("fallbacks").GetString());
        Assert.False(body.TryGetProperty("temperature", out _), "Claude Opus 5.5 rejects sampling parameters.");
        Assert.False(body.TryGetProperty("thinking", out _), "Thinking is adaptive by default and cannot be disabled on Claude Opus 5.5.");
    }

    [Fact]
    public async Task ReportsAServerSideFallback()
    {
        _server.Enqueue(AnthropicStreams.Text(["ok"], model: "claude-opus-4-8", withFallback: true));

        var response = await Provider().GenerateAsync(AiRequest.Create("test.fallback", string.Empty, "Hi"), null, CancellationToken.None);

        Assert.True(response.FellBack);
        Assert.Equal("claude-opus-4-8", response.Model);
        Assert.Equal("ok", response.Text);
    }

    [Theory]
    [InlineData("max_tokens", AiStopReason.MaxTokens)]
    [InlineData("refusal", AiStopReason.Refusal)]
    [InlineData("model_context_window_exceeded", AiStopReason.MaxTokens)]
    [InlineData("pause_turn", AiStopReason.Other)]
    public async Task ATruncatedOrRefusedJsonAnswerIsNotParsed(string stopReason, AiStopReason expected)
    {
        _server.Enqueue(AnthropicStreams.Text(["{\"answer\":\"4"], stopReason: stopReason));
        var request = AiRequest.Create("test.json", string.Empty, "Q") with { JsonSchema = Schema };

        var response = await Provider().GenerateAsync(request, null, CancellationToken.None);

        Assert.Equal(expected, response.StopReason);
        Assert.Null(response.Json);
    }

    [Fact]
    public async Task UnauthorizedIsAnInvalidKeyAndTheKeyNeverAppears()
    {
        _server.Enqueue(AnthropicStreams.Error(401, "authentication_error", "invalid x-api-key " + Key));

        var error = await Assert.ThrowsAsync<AiException>(() => Provider().GenerateAsync(AiRequest.Create("test.auth", string.Empty, "Hi"), null, CancellationToken.None));

        Assert.Equal(AiErrorCodes.InvalidKey, error.Code);
        Assert.Equal(401, error.Error.HttpStatus);
        Assert.Single(_server.Requests);
        Assert.StartsWith("Claude did not accept the saved API key. Nothing was sent twice and no document was changed.", error.Message, StringComparison.Ordinal);
        AssertNoKey(error);
    }

    [Fact]
    public async Task RateLimitWaitsForRetryAfterThenSucceeds()
    {
        _server.Enqueue(AnthropicStreams.Error(429, "rate_limit_error", "Number of requests has exceeded your rate limit", ("retry-after", "7")))
            .Enqueue(AnthropicStreams.Text(["done"]));
        var progress = new ProgressLog<AiProgress>();

        var response = await Provider().GenerateAsync(AiRequest.Create("test.retry", string.Empty, "Hi"), progress, CancellationToken.None);

        Assert.Equal("done", response.Text);
        Assert.Equal(2, response.Timings.Attempts);
        Assert.Equal(2, _server.Requests.Count);
        Assert.Contains(TimeSpan.FromSeconds(7), _time.Delays);
        var wait = Assert.Single(progress.Items, p => p.Stage == AiProgressStage.WaitingToRetry);
        Assert.Equal(TimeSpan.FromSeconds(7), wait.RetryIn);
        Assert.Equal(2, wait.Attempt);
        Assert.Contains(_log.Lines, l => l.Contains("HTTP 429", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ARetryAfterBeyondTheLimitFailsAtOnceWithTheWait()
    {
        _server.Enqueue(AnthropicStreams.Error(429, "rate_limit_error", "slow down", ("retry-after", "3600")));

        var error = await Assert.ThrowsAsync<AiException>(() => Provider().GenerateAsync(AiRequest.Create("test.retry", string.Empty, "Hi"), null, CancellationToken.None));

        Assert.Equal(AiErrorCodes.RateLimited, error.Code);
        Assert.Equal(TimeSpan.FromHours(1), error.Error.RetryAfter);
        Assert.Single(_server.Requests);
        Assert.Contains("Try again in 60 minutes.", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RateLimitStopsAfterTheLastAttempt()
    {
        for (var i = 0; i < 3; i++)
        {
            _server.Enqueue(AnthropicStreams.Error(429, "rate_limit_error", "slow down", ("retry-after", "1")));
        }

        var error = await Assert.ThrowsAsync<AiException>(() => Provider().GenerateAsync(AiRequest.Create("test.retry", string.Empty, "Hi"), null, CancellationToken.None));

        Assert.Equal(AiErrorCodes.RateLimited, error.Code);
        Assert.Equal(3, _server.Requests.Count);
    }

    [Fact]
    public async Task ServerErrorIsRetriedWithBackoffThenSucceeds()
    {
        _server.Enqueue(AnthropicStreams.Error(500, "api_error", "Internal server error"))
            .Enqueue(AnthropicStreams.Error(529, "overloaded_error", "Overloaded"))
            .Enqueue(AnthropicStreams.Text(["fine"]));

        var response = await Provider().GenerateAsync(AiRequest.Create("test.retry", string.Empty, "Hi"), null, CancellationToken.None);

        Assert.Equal("fine", response.Text);
        Assert.Equal(3, response.Timings.Attempts);
        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)], _time.Delays);
    }

    [Fact]
    public async Task AnOverloadEventBeforeAnyTextIsRetried()
    {
        _server.Enqueue(AnthropicStreams.OverloadedInStream()).Enqueue(AnthropicStreams.Text(["again"]));

        var response = await Provider().GenerateAsync(AiRequest.Create("test.retry", string.Empty, "Hi"), null, CancellationToken.None);

        Assert.Equal("again", response.Text);
        Assert.Equal(2, _server.Requests.Count);
    }

    [Fact]
    public async Task ABadRequestIsNotRetried()
    {
        _server.Enqueue(AnthropicStreams.Error(400, "invalid_request_error", "messages: roles must alternate"));

        var error = await Assert.ThrowsAsync<AiException>(() => Provider().GenerateAsync(AiRequest.Create("test.bad", string.Empty, "Hi"), null, CancellationToken.None));

        Assert.Equal(AiErrorCodes.ProviderError, error.Code);
        Assert.Single(_server.Requests);
        Assert.DoesNotContain("alternate", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APromptThatIsTooLongNamesTheNumbers()
    {
        _server.Enqueue(AnthropicStreams.Error(400, "invalid_request_error", "prompt is too long: 1050123 tokens > 1000000 maximum"));

        var error = await Assert.ThrowsAsync<AiException>(() => Provider().GenerateAsync(AiRequest.Create("test.long", string.Empty, "Hi"), null, CancellationToken.None));

        Assert.Equal(AiErrorCodes.ContentTooLong, error.Code);
        Assert.Contains("1,050,123 tokens, the limit is 1,000,000", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoAnswerWithinTheTimeoutIsANetworkErrorAndIsNotRetried()
    {
        _server.Enqueue(ScriptedResponse.Stall()).Enqueue(AnthropicStreams.Text(["never"]));
        var options = new AnthropicOptions { BaseUrl = _server.BaseUrl, Http = new CloudHttpOptions { ResponseTimeout = TimeSpan.FromMilliseconds(300) } };

        var error = await Assert.ThrowsAsync<AiException>(() => Provider(options).GenerateAsync(AiRequest.Create("test.timeout", string.Empty, "Hi"), null, CancellationToken.None));

        Assert.Equal(AiErrorCodes.Network, error.Code);
        Assert.Equal("Claude did not answer within 1 second. Nothing was sent twice and no document was changed. Try again; long inputs take longer.", error.Message);
        Assert.Single(_server.Requests);
    }

    [Fact]
    public async Task AStreamThatGoesQuietTimesOut()
    {
        _server.Enqueue(new ScriptedResponse
        {
            ContentType = "text/event-stream",
            Parts = [(ScriptedResponse.Format("message_start", """{"type":"message_start","message":{"model":"claude-opus-5-5","usage":{"input_tokens":1}}}"""), TimeSpan.Zero)],
            StallAfterBody = true,
        });
        var options = new AnthropicOptions { BaseUrl = _server.BaseUrl, Http = new CloudHttpOptions { StreamIdleTimeout = TimeSpan.FromMilliseconds(300) } };

        var error = await Assert.ThrowsAsync<AiException>(() => Provider(options).GenerateAsync(AiRequest.Create("test.idle", string.Empty, "Hi"), null, CancellationToken.None));

        Assert.Equal(AiErrorCodes.Network, error.Code);
    }

    [Fact]
    public async Task MalformedStreamJsonIsAProviderError()
    {
        _server.Enqueue(ScriptedResponse.Sse(("message_start", "{\"type\":\"message_start\",\"message\":{")));

        var error = await Assert.ThrowsAsync<AiException>(() => Provider().GenerateAsync(AiRequest.Create("test.malformed", string.Empty, "Hi"), null, CancellationToken.None));

        Assert.Equal(AiErrorCodes.ProviderError, error.Code);
        Assert.Equal("Claude sent an answer Memento could not read. Nothing was sent twice and no document was changed. Try again.", error.Message);
    }

    [Fact]
    public async Task ACompletedAnswerThatIsNotJsonIsAProviderError()
    {
        _server.Enqueue(AnthropicStreams.Text(["Sure! Here is the JSON: {"]));
        var request = AiRequest.Create("test.json", string.Empty, "Q") with { JsonSchema = Schema };

        var error = await Assert.ThrowsAsync<AiException>(() => Provider().GenerateAsync(request, null, CancellationToken.None));

        Assert.Equal(AiErrorCodes.ProviderError, error.Code);
    }

    [Fact]
    public async Task AStreamThatEndsWithoutMessageStopIsAProviderError()
    {
        _server.Enqueue(ScriptedResponse.Sse(("message_start", """{"type":"message_start","message":{"model":"claude-opus-5-5","usage":{"input_tokens":1}}}""")));

        var error = await Assert.ThrowsAsync<AiException>(() => Provider().GenerateAsync(AiRequest.Create("test.cut", string.Empty, "Hi"), null, CancellationToken.None));

        Assert.Equal(AiErrorCodes.ProviderError, error.Code);
    }

    [Fact]
    public async Task WithoutAKeyNothingIsSent()
    {
        var provider = new AnthropicProvider(_http, new FakeSecrets(), new AnthropicOptions { BaseUrl = _server.BaseUrl }, _log, _time);

        var readiness = await provider.CheckAsync(CancellationToken.None);
        var error = await Assert.ThrowsAsync<AiException>(() => provider.GenerateAsync(AiRequest.Create("test.nokey", string.Empty, "Hi"), null, CancellationToken.None));

        Assert.False(readiness.IsReady);
        Assert.Equal(AiErrorCodes.NoKey, readiness.Problem!.Code);
        Assert.Equal(AiErrorCodes.NoKey, error.Code);
        Assert.Empty(_server.Requests);
    }

    [Fact]
    public async Task AnUnreachableHostIsANetworkError()
    {
        var options = new AnthropicOptions { BaseUrl = new Uri("http://127.0.0.1:1/") };

        var error = await Assert.ThrowsAsync<AiException>(() => Provider(options).GenerateAsync(AiRequest.Create("test.down", string.Empty, "Hi"), null, CancellationToken.None));

        Assert.Equal(AiErrorCodes.Network, error.Code);
        AssertNoKey(error);
    }

    [Fact]
    public async Task AHostThatDoesNotResolveReadsAsNoNetwork()
    {
        var options = new AnthropicOptions { BaseUrl = new Uri("http://memento-test.invalid/") };

        var error = await Assert.ThrowsAsync<AiException>(() => Provider(options).GenerateAsync(AiRequest.Create("test.dns", string.Empty, "Hi"), null, CancellationToken.None));

        Assert.Equal(AiErrorCodes.Network, error.Code);
        Assert.Equal("Claude could not be reached: no network. Nothing was sent twice and no document was changed.", error.Message);
    }

    [Fact]
    public async Task CancellationThrowsOperationCanceled()
    {
        _server.Enqueue(ScriptedResponse.Stall());
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Provider().GenerateAsync(AiRequest.Create("test.cancel", string.Empty, "Hi"), null, cancel.Token));
    }

    [Fact]
    public async Task AGrammarWithoutASchemaIsRejectedBeforeSending()
    {
        var request = AiRequest.Create("test.grammar", string.Empty, "Hi") with { Grammar = "root ::= \"a\"" };

        await Assert.ThrowsAsync<ArgumentException>(() => Provider().GenerateAsync(request, null, CancellationToken.None));
        Assert.Empty(_server.Requests);
    }

    [Fact]
    public void CountsAreEstimatesThatErrHigh()
    {
        var provider = Provider();

        Assert.False(provider.Capabilities.ExactTokenCounts);
        Assert.True(provider.Capabilities.SupportsJsonSchema);
        Assert.Equal(1_000_000, provider.Capabilities.MaxContextTokens);
        Assert.True(provider.CountTokens("The quick brown fox jumps over the lazy dog.") >= 10);
    }

    private AnthropicProvider Provider(AnthropicOptions? options = null) =>
        new(_http, _secrets, options ?? new AnthropicOptions { BaseUrl = _server.BaseUrl }, _log, _time);

    private void AssertNoKey(AiException error)
    {
        Assert.DoesNotContain(Key, error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Key, error.Error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Key, _log.All, StringComparison.Ordinal);
        Assert.NotEmpty(_log.Lines);
    }
}
