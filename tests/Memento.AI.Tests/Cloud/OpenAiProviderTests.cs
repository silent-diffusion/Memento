using System.Text.Json;
using Memento.AI.Http;
using Memento.AI.OpenAI;
using Memento.AI.Tests.Fakes;

namespace Memento.AI.Tests.Cloud;

public sealed class OpenAiProviderTests : IDisposable
{
    private const string Key = "test-only-openai-key-4d8e2a6c9f";

    private static readonly JsonElement Schema = JsonDocument.Parse("""
        {"type":"object","properties":{"items":{"type":"array","items":{"type":"string"}}},"required":["items"],"additionalProperties":false}
        """).RootElement.Clone();

    private readonly ScriptedHttpServer _server = new();
    private readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly SpyLogger<OpenAiProvider> _log = new();
    private readonly InstantTimeProvider _time = new();
    private readonly FakeSecrets _secrets = new FakeSecrets().With("openai", Key);

    public void Dispose()
    {
        _http.Dispose();
        _server.Dispose();
    }

    [Fact]
    public async Task StreamsAStructuredAnswerThroughTheResponsesApi()
    {
        _server.Enqueue(OpenAiStreams.Text(["{\"items\":[", "\"a\",\"b\"]}"], input: 33, output: 12));
        var progress = new ProgressLog<AiProgress>();
        var request = new AiRequest
        {
            Purpose = "map.items",
            System = "Extract items.",
            Messages = [AiMessage.User("a and b")],
            MaxOutputTokens = 1000,
            JsonSchema = Schema,
            SchemaName = "map items!",
        };

        var response = await Provider().GenerateAsync(request, progress, CancellationToken.None);

        Assert.Equal(2, response.Json!.Value.GetProperty("items").GetArrayLength());
        Assert.Equal(AiStopReason.Completed, response.StopReason);
        Assert.Equal(new AiUsage(33, 12, 0), response.Usage);
        Assert.Equal("gpt-6-astra", response.Model);
        Assert.Equal(2, progress.Items.Count(p => p.Stage == AiProgressStage.Generating));

        var sent = Assert.Single(_server.Requests);
        Assert.Equal("/v1/responses", sent.Path);
        Assert.Equal("Bearer " + Key, sent.Header("Authorization"));
        var body = sent.Json;
        Assert.Equal("gpt-6-astra", body.GetProperty("model").GetString());
        Assert.False(body.GetProperty("store").GetBoolean());
        Assert.True(body.GetProperty("stream").GetBoolean());
        Assert.Equal("Extract items.", body.GetProperty("instructions").GetString());
        Assert.Equal(17_000, body.GetProperty("max_output_tokens").GetInt32());
        Assert.Equal("high", body.GetProperty("reasoning").GetProperty("effort").GetString());
        var format = body.GetProperty("text").GetProperty("format");
        Assert.Equal("json_schema", format.GetProperty("type").GetString());
        Assert.Equal("map_items_", format.GetProperty("name").GetString());
        Assert.True(format.GetProperty("strict").GetBoolean());
        Assert.Equal("array", format.GetProperty("schema").GetProperty("properties").GetProperty("items").GetProperty("type").GetString());
        Assert.Equal("user", body.GetProperty("input")[0].GetProperty("role").GetString());
    }

    [Fact]
    public async Task AnIncompleteAnswerReportsTheLimit()
    {
        _server.Enqueue(OpenAiStreams.Text(["{\"items\":[\"a"], status: "incomplete", incompleteReason: "max_output_tokens"));
        var request = AiRequest.Create("map.items", string.Empty, "x") with { JsonSchema = Schema };

        var response = await Provider().GenerateAsync(request, null, CancellationToken.None);

        Assert.Equal(AiStopReason.MaxTokens, response.StopReason);
        Assert.Equal("incomplete:max_output_tokens", response.ProviderStopReason);
        Assert.Null(response.Json);
    }

    [Fact]
    public async Task ARefusalIsReported()
    {
        _server.Enqueue(OpenAiStreams.Text([], refusal: true));

        var response = await Provider().GenerateAsync(AiRequest.Create("t", string.Empty, "x"), null, CancellationToken.None);

        Assert.Equal(AiStopReason.Refusal, response.StopReason);
    }

    [Fact]
    public async Task UnauthorizedNeverEchoesTheKeyOrItsMaskedForm()
    {
        _server.Enqueue(OpenAiStreams.Error(401, "invalid_request_error", "invalid_api_key", "Incorrect API key provided: sk-test_****9f00. You can find your API key at the dashboard."));

        var error = await Assert.ThrowsAsync<AiException>(() => Provider().GenerateAsync(AiRequest.Create("t", string.Empty, "x"), null, CancellationToken.None));

        Assert.Equal(AiErrorCodes.InvalidKey, error.Code);
        Assert.Single(_server.Requests);
        foreach (var text in new[] { error.ToString(), error.Error.ToString(), _log.All })
        {
            Assert.DoesNotContain(Key, text, StringComparison.Ordinal);
            Assert.DoesNotContain("sk-test", text, StringComparison.Ordinal);
            Assert.DoesNotContain("9f00", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task RateLimitHonoursRetryAfterMilliseconds()
    {
        _server.Enqueue(OpenAiStreams.Error(429, "requests", "rate_limit_exceeded", "Rate limit reached", ("retry-after-ms", "1500"), ("retry-after", "2")))
            .Enqueue(OpenAiStreams.Text(["ok"]));

        var response = await Provider().GenerateAsync(AiRequest.Create("t", string.Empty, "x"), null, CancellationToken.None);

        Assert.Equal("ok", response.Text);
        Assert.Equal(2, _server.Requests.Count);
        Assert.Contains(TimeSpan.FromMilliseconds(1500), _time.Delays);
    }

    [Fact]
    public async Task ExhaustedQuotaIsNotRetried()
    {
        _server.Enqueue(OpenAiStreams.Error(429, "insufficient_quota", "insufficient_quota", "You exceeded your current quota"));

        var error = await Assert.ThrowsAsync<AiException>(() => Provider().GenerateAsync(AiRequest.Create("t", string.Empty, "x"), null, CancellationToken.None));

        Assert.Equal(AiErrorCodes.ProviderError, error.Code);
        Assert.Contains("no credit or quota left", error.Message, StringComparison.Ordinal);
        Assert.Single(_server.Requests);
    }

    [Fact]
    public async Task ServerErrorThenSuccess()
    {
        _server.Enqueue(OpenAiStreams.Error(500, "server_error", null, "The server had an error"))
            .Enqueue(OpenAiStreams.Text(["recovered"]));

        var response = await Provider().GenerateAsync(AiRequest.Create("t", string.Empty, "x"), null, CancellationToken.None);

        Assert.Equal("recovered", response.Text);
        Assert.Equal(2, response.Timings.Attempts);
    }

    [Fact]
    public async Task APersistentServerErrorNamesTheStatus()
    {
        for (var i = 0; i < 3; i++)
        {
            _server.Enqueue(OpenAiStreams.Error(503, "server_error", null, "unavailable"));
        }

        var error = await Assert.ThrowsAsync<AiException>(() => Provider().GenerateAsync(AiRequest.Create("t", string.Empty, "x"), null, CancellationToken.None));

        Assert.Equal(AiErrorCodes.ProviderError, error.Code);
        Assert.Equal("ChatGPT had a problem on its side (HTTP 503). Nothing was sent twice and no document was changed. Try again in a few minutes.", error.Message);
        Assert.Equal(3, _server.Requests.Count);
    }

    [Fact]
    public async Task ContextLengthExceededIsContentTooLong()
    {
        _server.Enqueue(OpenAiStreams.Error(400, "invalid_request_error", "context_length_exceeded", "This model's maximum context length is 1050000 tokens. However, your messages resulted in 1100000 tokens."));

        var error = await Assert.ThrowsAsync<AiException>(() => Provider().GenerateAsync(AiRequest.Create("t", string.Empty, "x"), null, CancellationToken.None));

        Assert.Equal(AiErrorCodes.ContentTooLong, error.Code);
        Assert.Contains("1,100,000 tokens, the limit is 1,050,000", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TimeoutIsANetworkError()
    {
        _server.Enqueue(ScriptedResponse.Stall());
        var options = new OpenAiOptions { BaseUrl = _server.BaseUrl, Http = new CloudHttpOptions { ResponseTimeout = TimeSpan.FromMilliseconds(300) } };

        var error = await Assert.ThrowsAsync<AiException>(() => Provider(options).GenerateAsync(AiRequest.Create("t", string.Empty, "x"), null, CancellationToken.None));

        Assert.Equal(AiErrorCodes.Network, error.Code);
        Assert.Single(_server.Requests);
    }

    [Fact]
    public async Task MalformedJsonIsAProviderError()
    {
        _server.Enqueue(ScriptedResponse.Sse(("response.output_text.delta", "{\"type\":\"response.output_text.delta\",\"delta\":")));

        var error = await Assert.ThrowsAsync<AiException>(() => Provider().GenerateAsync(AiRequest.Create("t", string.Empty, "x"), null, CancellationToken.None));

        Assert.Equal(AiErrorCodes.ProviderError, error.Code);
    }

    [Fact]
    public async Task AFailedResponseEventAfterTextIsNotRetried()
    {
        _server.Enqueue(ScriptedResponse.Sse(
            ("response.output_text.delta", """{"type":"response.output_text.delta","delta":"partial"}"""),
            ("response.failed", """{"type":"response.failed","response":{"status":"failed","error":{"code":"server_error","message":"boom"}}}""")));

        var error = await Assert.ThrowsAsync<AiException>(() => Provider().GenerateAsync(AiRequest.Create("t", string.Empty, "x"), null, CancellationToken.None));

        Assert.Equal(AiErrorCodes.ProviderError, error.Code);
        Assert.Single(_server.Requests);
    }

    [Fact]
    public async Task ReadinessNeedsAKeyAndSendsNothing()
    {
        var ready = await Provider().CheckAsync(CancellationToken.None);
        var missing = await new OpenAiProvider(_http, new FakeSecrets(), new OpenAiOptions { BaseUrl = _server.BaseUrl }, _log).CheckAsync(CancellationToken.None);

        Assert.True(ready.IsReady);
        Assert.False(missing.IsReady);
        Assert.Equal("ChatGPT has no API key saved, so nothing was sent and no document was changed. Add the key in Settings › AI and privacy.", missing.Problem!.Message);
        Assert.Empty(_server.Requests);
        Assert.Equal(0, _secrets.Reads);
    }

    [Theory]
    [InlineData("result", "result")]
    [InlineData("", "result")]
    [InlineData("a.b c", "a_b_c")]
    public void SchemaNamesAreSanitised(string name, string expected) => Assert.Equal(expected, OpenAiProvider.SchemaName(name));

    private OpenAiProvider Provider(OpenAiOptions? options = null) =>
        new(_http, _secrets, options ?? new OpenAiOptions { BaseUrl = _server.BaseUrl }, _log, _time);
}
