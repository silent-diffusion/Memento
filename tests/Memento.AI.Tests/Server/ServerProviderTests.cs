using System.Globalization;
using System.Text;
using System.Text.Json;
using Memento.AI.Http;
using Memento.AI.Server;
using Memento.AI.Tests.Fakes;
using Memento.Core.Ai;

namespace Memento.AI.Tests.Server;

/// <summary>
/// "Your own server" (2.5.0) against fake Ollama, LM Studio and vLLM servers on 127.0.0.1: the request it sends, the
/// three ways servers stream, the json_schema fallback, the key rule, and every failure with its specific sentence. No
/// real server and no real API is contacted.
/// </summary>
public sealed class ServerProviderTests : IDisposable
{
    private const string Key = "test-only-server-key";

    private static readonly JsonElement Schema = JsonDocument.Parse("""
        {"type":"object","properties":{"items":{"type":"array","items":{"type":"string"}}},"required":["items"],"additionalProperties":false}
        """).RootElement.Clone();

    private readonly AiHttpClient _http = new();
    private readonly InstantTimeProvider _time = new();
    private readonly SpyLogger<ServerProvider> _log = new();
    private readonly List<IDisposable> _servers = [];

    public void Dispose()
    {
        _http.Dispose();
        foreach (var server in _servers)
        {
            server.Dispose();
        }
    }

    [Fact]
    public async Task AnOllamaShapedServerGetsOneStreamedChatRequestWithAStrictSchema()
    {
        var server = Fake(ServerPersonality.Ollama);
        var progress = new ProgressLog<AiProgress>();
        var request = Structured() with { Temperature = 0 };

        var response = await Provider(server, "qwen3:8b").GenerateAsync(request, progress, CancellationToken.None);

        Assert.Equal(["a", "b"], response.Json!.Value.GetProperty("items").EnumerateArray().Select(i => i.GetString()));
        Assert.Equal(AiStopReason.Completed, response.StopReason);
        Assert.Equal("stop", response.ProviderStopReason);
        Assert.Equal(AiUsage.None, response.Usage); // Ollama sends no usage chunk.
        Assert.Equal(("server", "qwen3:8b"), (response.ProviderId, response.Model));
        Assert.Contains(progress.Items, p => p.Stage == AiProgressStage.Generating);

        var sent = Assert.Single(server.Requests);
        Assert.Equal(("POST", "/v1/chat/completions"), (sent.Method, sent.Path));
        Assert.Null(sent.Header("Authorization"));
        var body = sent.Json;
        Assert.Equal("qwen3:8b", body.GetProperty("model").GetString());
        Assert.True(body.GetProperty("stream").GetBoolean());
        Assert.Equal(0, body.GetProperty("temperature").GetDouble());
        Assert.Equal(1000, body.GetProperty("max_tokens").GetInt32());
        Assert.Equal(["system", "user"], body.GetProperty("messages").EnumerateArray().Select(m => m.GetProperty("role").GetString()));
        Assert.Equal("Extract items.", body.GetProperty("messages")[0].GetProperty("content").GetString());
        Assert.Equal("a and b", body.GetProperty("messages")[1].GetProperty("content").GetString());
        var format = body.GetProperty("response_format");
        Assert.Equal("json_schema", format.GetProperty("type").GetString());
        Assert.Equal("map_items_", format.GetProperty("json_schema").GetProperty("name").GetString());
        Assert.True(format.GetProperty("json_schema").GetProperty("strict").GetBoolean());
        Assert.Equal("array", format.GetProperty("json_schema").GetProperty("schema").GetProperty("properties").GetProperty("items").GetProperty("type").GetString());
    }

    [Fact]
    public async Task AnLmStudioShapedServersUsageChunkAfterTheLastTextIsRead()
    {
        var server = Fake(ServerPersonality.LmStudio);

        var response = await Provider(server, "qwen/qwen3-8b").GenerateAsync(Structured(), null, CancellationToken.None);

        Assert.Equal(new AiUsage(120, 7), response.Usage);
        Assert.Equal(AiStopReason.Completed, response.StopReason);
        Assert.Equal(2, response.Json!.Value.GetProperty("items").GetArrayLength());
        Assert.Single(server.Requests);
    }

    [Fact]
    public async Task AVllmShapedServerThatRefusesTheSchemaIsAskedOnceForAJsonObjectAndRemembered()
    {
        var server = Fake(ServerPersonality.Vllm);
        var provider = Provider(server, "Qwen/Qwen3-8B");
        Assert.True(provider.Capabilities.SupportsJsonSchema);

        var first = await provider.GenerateAsync(Structured(), null, CancellationToken.None);

        Assert.Equal(2, first.Json!.Value.GetProperty("items").GetArrayLength());
        var requests = server.Completions;
        Assert.Equal(2, requests.Count);
        Assert.Equal("json_schema", requests[0].Json.GetProperty("response_format").GetProperty("type").GetString());
        var fallback = requests[1].Json;
        Assert.Equal("json_object", fallback.GetProperty("response_format").GetProperty("type").GetString());
        var system = fallback.GetProperty("messages")[0].GetProperty("content").GetString()!;
        Assert.StartsWith("Extract items.\n\nAnswer with one JSON object that matches this JSON Schema", system, StringComparison.Ordinal);
        Assert.Contains("\"additionalProperties\":false", system, StringComparison.Ordinal);
        Assert.False(provider.Capabilities.SupportsJsonSchema);
        Assert.Contains(_log.Lines, l => l.Contains("refused a JSON schema", StringComparison.Ordinal));

        // For as long as Memento runs, this server is asked for a JSON object straight away.
        await Provider(server, "Qwen/Qwen3-8B").GenerateAsync(Structured(), null, CancellationToken.None);
        Assert.Equal(3, server.Completions.Count);
        Assert.Equal("json_object", server.Completions[2].Json.GetProperty("response_format").GetProperty("type").GetString());

        // Another address starts trusted again.
        var other = Fake(ServerPersonality.Vllm);
        Assert.True(Provider(other, "Qwen/Qwen3-8B").Capabilities.SupportsJsonSchema);
    }

    [Fact]
    public async Task ASavedKeyIsSentAsABearerTokenAndOnlyThen()
    {
        var server = Fake(ServerPersonality.Vllm);
        server.RequiredKey = Key;

        var response = await Provider(server, "Qwen/Qwen3-8B", new FakeSecrets().With("server", Key)).GenerateAsync(AiRequest.Create("map.text", "Be brief.", "Hello"), null, CancellationToken.None);

        Assert.Equal("{\"items\":[\"a\",\"b\"]}", response.Text);
        Assert.Null(response.Json); // No schema was asked for.
        Assert.Equal("Bearer " + Key, Assert.Single(server.Requests).Header("Authorization"));
        Assert.False(Assert.Single(server.Requests).Json.TryGetProperty("response_format", out _));
        Assert.DoesNotContain(Key, _log.All, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NothingListeningIsTheBriefsSentence()
    {
        var port = FakeOpenAiCompatibleServer.ClosedPort();
        var address = string.Create(CultureInfo.InvariantCulture, $"http://127.0.0.1:{port}");

        var error = await Assert.ThrowsAsync<AiException>(() => Provider(address, "qwen3:8b").GenerateAsync(Structured(), null, CancellationToken.None));

        Assert.Equal(AiErrorCodes.ServerUnreachable, error.Code);
        Assert.Equal($"Nothing is listening at {address}. Nothing was sent and no document was changed. Start Ollama or LM Studio, or check the address in Settings › AI and privacy.", error.Message);
        Assert.Equal("ConnectionRefused", error.Error.Diagnostic);
    }

    [Theory]
    [InlineData(false, "asks for a key (HTTP 401)")]
    [InlineData(true, "did not accept the saved key (HTTP 401)")]
    public async Task A401SaysTheServerNeedsAKey(bool saved, string expected)
    {
        var server = Fake(ServerPersonality.LmStudio);
        server.RequiredKey = Key;
        var secrets = saved ? new FakeSecrets().With("server", "test-only-wrong-key") : new FakeSecrets();

        var error = await Assert.ThrowsAsync<AiException>(() => Provider(server, "qwen/qwen3-8b", secrets).GenerateAsync(Structured(), null, CancellationToken.None));

        Assert.Equal(AiErrorCodes.ServerNeedsKey, error.Code);
        Assert.Contains($"The server at {server.Address} {expected}", error.Message, StringComparison.Ordinal);
        Assert.Contains("no document was changed", error.Message, StringComparison.Ordinal);
        Assert.Single(server.Requests);
    }

    [Theory]
    [InlineData(302)]
    [InlineData(307)]
    [InlineData(308)]
    public async Task ARedirectIsRefusedSoNeitherTheKeyNorTheTextGoesElsewhere(int status)
    {
        var server = Fake(ServerPersonality.Ollama);
        var elsewhere = Fake(ServerPersonality.Ollama);
        server.Override = _ => ScriptedResponse.Json(status, "{}", ("Location", elsewhere.Address + "/v1/chat/completions"));

        var error = await Assert.ThrowsAsync<AiException>(() => Provider(server, "qwen3:8b", new FakeSecrets().With("server", Key)).GenerateAsync(AiRequest.Create("map.text", "Be brief.", "The confidential transcript."), null, CancellationToken.None));

        Assert.Equal(AiErrorCodes.ProviderError, error.Code);
        Assert.Contains($"The server at {server.Address} answered with a redirect to 127.0.0.1", error.Message, StringComparison.Ordinal);
        Assert.Contains("Memento does not follow redirects", error.Message, StringComparison.Ordinal);
        Assert.Single(server.Requests);
        Assert.Empty(elsewhere.Requests);
        Assert.DoesNotContain(Key, error.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ServerPersonality.Ollama)]
    [InlineData(ServerPersonality.LmStudio)]
    [InlineData(ServerPersonality.Vllm)]
    public async Task AModelTheServerDoesNotOfferIsNamedAndNothingElseIsTried(ServerPersonality personality)
    {
        var server = Fake(personality);

        var error = await Assert.ThrowsAsync<AiException>(() => Provider(server, "qwen9:1b").GenerateAsync(Structured(), null, CancellationToken.None));

        Assert.Equal(AiErrorCodes.ServerModelUnknown, error.Code);
        Assert.Equal($"The server at {server.Address} does not offer a model called 'qwen9:1b'. Nothing was generated and no document was changed. Choose one of its models in Settings › AI and privacy (Test connection lists them), or check the name.", error.Message);
        Assert.Equal(404, error.Error.HttpStatus);
        Assert.Single(server.Requests);
    }

    [Fact]
    public async Task AFiveMebibyteAnswerIsCappedAndReportedAsUnreadable()
    {
        var server = Fake(ServerPersonality.Ollama);
        var piece = new string('x', 64 * 1024);
        server.Override = request => request.Path == "/v1/chat/completions"
            ? new ScriptedResponse
            {
                ContentType = "text/event-stream",
                Parts = Enumerable.Range(0, 80).Select(_ => ("data: " + FakeOpenAiCompatibleServer.Chunk("qwen3:8b", new() { ["content"] = piece }, null) + "\n\n", TimeSpan.Zero)).ToList(),
            }
            : null;

        var error = await Assert.ThrowsAsync<AiException>(() => Provider(server, "qwen3:8b").GenerateAsync(Structured(), null, CancellationToken.None));

        Assert.Equal(AiErrorCodes.ProviderError, error.Code);
        Assert.Equal("the answer is too long", error.Error.Diagnostic);
        Assert.Contains($"The server at {server.Address} sent an answer Memento could not read.", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OneEndlessStreamLineIsCappedToo()
    {
        var server = Fake(ServerPersonality.LmStudio);
        server.Override = request => request.Path == "/v1/chat/completions"
            ? new ScriptedResponse { ContentType = "text/event-stream", Parts = [("data: " + new string('y', SseReader.MaxEventChars + 10), TimeSpan.Zero)] }
            : null;

        var error = await Assert.ThrowsAsync<AiException>(() => Provider(server, "qwen/qwen3-8b").GenerateAsync(Structured(), null, CancellationToken.None));

        Assert.Equal("the answer is too long", error.Error.Diagnostic);
    }

    [Fact]
    public async Task AnAnswerThatIsNotJsonIsUnreadable()
    {
        var server = Fake(ServerPersonality.Ollama);
        server.Answer = _ => "Sure! The items are a and b.";

        var error = await Assert.ThrowsAsync<AiException>(() => Provider(server, "qwen3:8b").GenerateAsync(Structured(), null, CancellationToken.None));

        Assert.Equal(AiErrorCodes.ProviderError, error.Code);
        Assert.Equal("the answer is not valid JSON", error.Error.Diagnostic);
        Assert.Contains("could not read", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AWrappedJsonAnswerIsUnwrapped()
    {
        var server = Fake(ServerPersonality.Vllm);
        server.Answer = _ => "<think>The user wants items.</think>\n```json\n{\"items\":[\"a\"]}\n```";

        var response = await Provider(server, "Qwen/Qwen3-8B").GenerateAsync(Structured(), null, CancellationToken.None);

        Assert.Equal(1, response.Json!.Value.GetProperty("items").GetArrayLength());
        Assert.StartsWith("<think>", response.Text, StringComparison.Ordinal); // The text is kept as the server sent it.
    }

    [Theory]
    [InlineData("""{"object":"error","message":"This model's maximum context length is 8192 tokens. However, you requested 9000 tokens (8000 in the messages, 1000 in the completion).","type":"BadRequestError","param":null,"code":400}""", "9,000 tokens, the server's limit is 8,192")]
    [InlineData("""{"error":{"code":400,"message":"the request exceeds the available context size, try increasing it","type":"exceed_context_size_error","n_prompt_tokens":9000,"n_ctx":8192}}""", "The text is too long for qwen3:8b at")]
    public async Task AContextOverflowIsTheTooLongError(string body, string expected)
    {
        var server = Fake(ServerPersonality.Vllm);
        server.Override = request => request.Path == "/v1/chat/completions" ? ScriptedResponse.Json(400, body) : null;

        var error = await Assert.ThrowsAsync<AiException>(() => Provider(server, "qwen3:8b").GenerateAsync(AiRequest.Create("map.text", "Be brief.", "x"), null, CancellationToken.None));

        Assert.Equal(AiErrorCodes.ContentTooLong, error.Code);
        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
        Assert.Contains("Set the context in Settings › AI and privacy to what the server runs", error.Message, StringComparison.Ordinal);
        Assert.Single(server.Requests);
    }

    [Fact]
    public async Task AServerThatAnswersWithOneJsonErrorInsteadOfAStreamIsRead()
    {
        // LM Studio sends one JSON document, not a stream, for some errors.
        var server = Fake(ServerPersonality.LmStudio);
        server.Override = request => request.Path == "/v1/chat/completions"
            ? ScriptedResponse.Json(200, """{"error":"Trying to keep the first 9000 tokens when context the overflows. However, the model is loaded with context length of only 4096 tokens, which is not enough."}""")
            : null;

        var error = await Assert.ThrowsAsync<AiException>(() => Provider(server, "qwen/qwen3-8b").GenerateAsync(Structured(), null, CancellationToken.None));

        Assert.Equal(AiErrorCodes.ContentTooLong, error.Code);
        Assert.Contains(server.Address, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AServerThatIgnoresStreamingAndSendsOneCompletionIsRead()
    {
        var server = Fake(ServerPersonality.LmStudio);
        server.Override = request => request.Path == "/v1/chat/completions"
            ? new ScriptedResponse
            {
                ContentType = "text/event-stream", // Mislabelled: the body is one JSON document.
                Parts = [("""{"id":"x","object":"chat.completion","model":"qwen/qwen3-8b","choices":[{"index":0,"message":{"role":"assistant","content":"{\"items\":[\"z\"]}"},"finish_reason":"stop"}],"usage":{"prompt_tokens":40,"completion_tokens":5}}""", TimeSpan.Zero)],
            }
            : null;

        var response = await Provider(server, "qwen/qwen3-8b").GenerateAsync(Structured(), null, CancellationToken.None);

        Assert.Equal("z", response.Json!.Value.GetProperty("items")[0].GetString());
        Assert.Equal(new AiUsage(40, 5), response.Usage);
    }

    [Fact]
    public async Task AStreamThatEndsAfterItsFinishReasonWithoutDoneIsComplete()
    {
        var server = Fake(ServerPersonality.Ollama);
        server.SendDone = false;

        var response = await Provider(server, "qwen3:8b").GenerateAsync(Structured(), null, CancellationToken.None);

        Assert.Equal(AiStopReason.Completed, response.StopReason);
    }

    [Fact]
    public async Task AStreamCutOffBeforeItsEndFails()
    {
        var server = Fake(ServerPersonality.Ollama);
        server.Override = request => request.Path == "/v1/chat/completions"
            ? new ScriptedResponse { ContentType = "text/event-stream", Parts = [("data: " + FakeOpenAiCompatibleServer.Chunk("qwen3:8b", new() { ["content"] = "{\"items\":" }, null) + "\n\n", TimeSpan.Zero)] }
            : null;

        var error = await Assert.ThrowsAsync<AiException>(() => Provider(server, "qwen3:8b").GenerateAsync(Structured(), null, CancellationToken.None));

        Assert.Equal(AiErrorCodes.ProviderError, error.Code);
        Assert.Contains("stopped in the middle of its answer", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ALengthStopIsReportedAndNotParsed()
    {
        var server = Fake(ServerPersonality.Ollama);
        server.Override = request => request.Path == "/v1/chat/completions"
            ? new ScriptedResponse
            {
                ContentType = "text/event-stream",
                Parts = [("data: " + FakeOpenAiCompatibleServer.Chunk("qwen3:8b", new() { ["content"] = "{\"items\":[\"a" }, "length") + "\n\ndata: [DONE]\n\n", TimeSpan.Zero)],
            }
            : null;

        var response = await Provider(server, "qwen3:8b").GenerateAsync(Structured(), null, CancellationToken.None);

        Assert.Equal(AiStopReason.MaxTokens, response.StopReason);
        Assert.Null(response.Json);
    }

    [Fact]
    public async Task APageThatIsNotAnApiIsNotCompatible()
    {
        var server = Fake(ServerPersonality.Ollama);
        server.Override = _ => new ScriptedResponse { Status = 404, ContentType = "text/html", Parts = [("<html>Not found</html>", TimeSpan.Zero)] };

        var error = await Assert.ThrowsAsync<AiException>(() => Provider(server, "qwen3:8b").GenerateAsync(Structured(), null, CancellationToken.None));

        Assert.Equal(AiErrorCodes.ServerNotCompatible, error.Code);
        Assert.Contains("does not answer like an OpenAI-compatible server (HTTP 404 at /v1/chat/completions)", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlainHttpOutsideTheLocalNetworkIsNeverSent()
    {
        var handler = new RecordingHttpHandler(System.Net.HttpStatusCode.OK);
        using var http = new AiHttpClient(handler);
        var address = ServerAddress.Parse("http://203.0.113.10:8000", out _)!;
        var provider = new ServerProvider(http.Client, new FakeSecrets().With("server", Key), new ServerOptions(address, "qwen3:8b"), _log, _time);

        var error = await Assert.ThrowsAsync<AiException>(() => provider.GenerateAsync(Structured(), null, CancellationToken.None));
        var readiness = await provider.CheckAsync(CancellationToken.None);

        Assert.Equal(AiErrorCodes.ServerInsecure, error.Code);
        Assert.False(readiness.IsReady);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task TheAnswerLimitLeavesRoomForThePromptInTheServersContext()
    {
        var server = Fake(ServerPersonality.Ollama);
        var request = AiRequest.Create("map.text", "Be brief.", string.Join(' ', Enumerable.Repeat("word", 600)), maxOutputTokens: 8000);

        await new ServerProvider(_http.Client, new FakeSecrets(), new ServerOptions(Address(server), "qwen3:8b", 2048), _log, _time).GenerateAsync(request, null, CancellationToken.None);

        var limit = Assert.Single(server.Requests).Json.GetProperty("max_tokens").GetInt32();
        Assert.InRange(limit, 256, 2048 - 600);
    }

    [Fact]
    public void ItCountsTokensWithTheMostCautiousEstimate()
    {
        var provider = Provider("http://localhost:11434", "qwen3:8b");
        const string text = "The quarterly numbers are in, and the release moves to November.";

        Assert.Equal(EstimatingTokenCounter.Server.Count(text), provider.CountTokens(text));
        Assert.True(EstimatingTokenCounter.Server.Factor > Math.Max(EstimatingTokenCounter.Claude.Factor, EstimatingTokenCounter.Generic.Factor));
        Assert.Equal(AiProviderKind.Server, provider.Kind);
        Assert.False(provider.LeavesPc);
        Assert.Equal((8192, 4096, false, true, false), (provider.Capabilities.MaxContextTokens, provider.Capabilities.MaxOutputTokens, provider.Capabilities.SupportsGrammar, provider.Capabilities.SupportsStreaming, provider.Capabilities.ExactTokenCounts));
    }

    [Theory]
    [InlineData("{\"a\":1}", "{\"a\":1}")]
    [InlineData("<think>\nhmm {not this}\n</think>\n{\"a\":1}", "{\"a\":1}")]
    [InlineData("```json\n{\"a\":1}\n```", "{\"a\":1}")]
    [InlineData("Here it is: {\"a\":{\"b\":2}} Done.", "{\"a\":{\"b\":2}}")]
    public void UnwrapKeepsOnlyTheObject(string text, string expected) => Assert.Equal(expected, ServerProvider.Unwrap(text));

    private static AiRequest Structured() => new()
    {
        Purpose = "map.items",
        System = "Extract items.",
        Messages = [AiMessage.User("a and b")],
        MaxOutputTokens = 1000,
        JsonSchema = Schema,
        SchemaName = "map items!",
    };

    private FakeOpenAiCompatibleServer Fake(ServerPersonality personality)
    {
        var server = new FakeOpenAiCompatibleServer(personality);
        _servers.Add(server);
        return server;
    }

    private static ServerAddress Address(FakeOpenAiCompatibleServer server) => ServerAddress.Parse(server.Address, out _)!;

    private ServerProvider Provider(FakeOpenAiCompatibleServer server, string model, FakeSecrets? secrets = null) =>
        new(_http.Client, secrets ?? new FakeSecrets(), new ServerOptions(Address(server), model), _log, _time);

    private ServerProvider Provider(string address, string model) =>
        new(_http.Client, new FakeSecrets(), new ServerOptions(ServerAddress.Parse(address, out _)!, model), _log, _time);
}
