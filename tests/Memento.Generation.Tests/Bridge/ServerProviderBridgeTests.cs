using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Memento.AI.Tests.Fakes;
using Memento.Core.Bridge;
using Memento.Core.Secrets;
using Memento.Generation.Tests.Support;

namespace Memento.Generation.Tests.Bridge;

/// <summary>
/// "Your own server" (2.5.0) through the bridge: readiness for each code, every consent decision keyed on whether the
/// address leaves the PC (not on the provider's kind), the record and History, the preview, and "Test connection". The
/// generations use the synthetic meeting's stand-in; the probe talks to a fake Ollama on 127.0.0.1.
/// </summary>
public sealed class ServerProviderBridgeTests : IDisposable
{
    private const string Network = "http://192.168.1.20:11434";

    private readonly M4Host _host = new();
    private readonly List<IDisposable> _servers = [];

    public void Dispose()
    {
        _host.Dispose();
        foreach (var server in _servers)
        {
            server.Dispose();
        }
    }

    [Fact]
    public async Task ReadinessComesFromSettingsAloneForEachCode()
    {
        Assert.Equal((false, DomainErrorCodes.AiNoServer, "No server address"), await StatusAsync());

        // Plain http outside the local network comes before external AI being off.
        await ServerAsync("http://203.0.113.10:8000", model: null);
        Assert.Equal((false, DomainErrorCodes.AiServerInsecure, "Needs https"), await StatusAsync());

        await ServerAsync(Network, model: null);
        Assert.Equal((false, DomainErrorCodes.AiDisabled, "External AI is off"), await StatusAsync());
        Assert.Contains($"The server at {Network} is not on this PC", (await InfoAsync()).GetProperty("detail").GetString(), StringComparison.Ordinal);

        await _host.ResultAsync("settings.set", new { ai = new { enabled = true } });
        Assert.Equal((false, DomainErrorCodes.AiNoModel, "No model chosen"), await StatusAsync());

        await ServerAsync("http://203.0.113.10:8000", "qwen3:8b");
        Assert.Equal((false, DomainErrorCodes.AiServerInsecure, "Needs https"), await StatusAsync());

        await ServerAsync("https://llm.example.com", "qwen3:8b");
        Assert.Equal((true, null, null), await StatusAsync());
        Assert.True((await InfoAsync()).GetProperty("leavesPc").GetBoolean());

        // A server on this PC needs no "Allow external AI services".
        await _host.ResultAsync("settings.set", new { ai = new { enabled = false } });
        await ServerAsync("http://localhost:11434", "qwen3:8b");
        Assert.Equal((true, null, null), await StatusAsync());
        var info = await InfoAsync();
        Assert.Equal("Your own server", info.GetProperty("name").GetString());
        Assert.Equal("Your own server", info.GetProperty("vendor").GetString());
        Assert.Equal("server", info.GetProperty("kind").GetString());
        Assert.Equal("qwen3:8b at localhost:11434", info.GetProperty("modelLabel").GetString());
        Assert.Equal("http://localhost:11434", info.GetProperty("address").GetString());
        Assert.False(info.GetProperty("leavesPc").GetBoolean());
        Assert.Equal(JsonValueKind.Null, info.GetProperty("modelListed").ValueKind);
        Assert.Equal(JsonValueKind.Null, info.GetProperty("modelId").ValueKind);
        Assert.Equal("Sends the ticked inputs to qwen3:8b at http://localhost:11434 when you press Generate. The server runs on this PC, so nothing leaves it. Memento assumes the server gives the model 8,192 tokens of context, as set in Settings (Ollama gives 4,096 unless num_ctx was raised); a long transcript is split into segments of up to 2,048 tokens.", info.GetProperty("detail").GetString());

        // The other providers say whether they leave the PC too; the server is listed last.
        var list = (await _host.ResultAsync("providers.list", new { })).GetProperty("providers").EnumerateArray().ToList();
        Assert.Equal(["anthropic", "openai", "local", "server"], list.Select(p => p.GetProperty("id").GetString()));
        Assert.Equal([true, true, false, false], list.Select(p => p.GetProperty("leavesPc").GetBoolean()));
        Assert.All(list.Take(3), p => Assert.Equal(JsonValueKind.Null, p.GetProperty("address").ValueKind));
        Assert.Equal(0, _host.Providers.ServerCreated + _host.Providers.CloudCreated + _host.Providers.LocalCreated);
    }

    [Fact]
    public async Task AServerOnThisPcRunsWithExternalAiOffWithoutAskingAndSaysThisPc()
    {
        var id = await _host.CreateMeetingAsync();
        await ServerAsync("http://localhost:11434", "qwen3:8b", contextTokens: 16384);
        Assert.False(_host.Host.Settings.Current.Ai.Enabled);
        Assert.True(_host.Host.Settings.Current.Ai.AskBeforeSend);

        var start = await _host.ResultAsync("generation.start", new { recordingId = id, template = await _host.MeetingMinutesAsync("server") });
        Assert.False(start.GetProperty("confirmationRequired").GetBoolean());
        var done = await _host.FinishedAsync(start.GetProperty("jobId").GetString()!);

        Assert.Equal("done", done.GetProperty("stage").GetString());
        var options = Assert.Single(_host.Providers.Server);
        Assert.Equal(("http://localhost:11434", "qwen3:8b", 16384), (options.Address.Text, options.Model, options.ContextTokens));
        var record = (await _host.ResultAsync("documents.get", new { recordingId = id, documentId = done.GetProperty("documentId").GetString() })).GetProperty("document").GetProperty("record");
        Assert.True(record.GetProperty("stayedOnPc").GetBoolean());
        Assert.Equal("server", record.GetProperty("providerId").GetString());
        Assert.Equal("qwen3:8b at localhost:11434 (this PC)", record.GetProperty("modelLabel").GetString());
        var line = Assert.Single(await _host.Host.Store.ReadHistoryAsync(id, CancellationToken.None), h => h.Summary.StartsWith("Meeting minutes generated", StringComparison.Ordinal));
        Assert.Equal("Meeting minutes generated with Your own server (qwen3:8b at localhost:11434)", line.Summary);
        Assert.Contains("sent to Your own server (qwen3:8b at localhost:11434 (this PC)); nothing left this PC", line.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheLiveOutputStreamsEachServerReplyWithAGrowingCountAndRealTotals()
    {
        // The real provider against a fake Ollama (no usage chunk), answering for the synthetic meeting.
        var server = new FakeOpenAiCompatibleServer(ServerPersonality.Ollama) { Answer = MeetingProvider.AnswerChatRequest };
        _servers.Add(server);
        var http = new Memento.AI.Http.AiHttpClient();
        _servers.Add(http);
        _host.Providers.MakeServer = options => new Memento.AI.Server.ServerProvider(http.Client, _host.Get<Memento.AI.ISecretReader>(), options, Microsoft.Extensions.Logging.Abstractions.NullLogger<Memento.AI.Server.ServerProvider>.Instance);
        var id = await _host.CreateMeetingAsync();
        await ServerAsync(server.Address, "qwen3:8b", contextTokens: 32768);

        var start = await _host.ResultAsync("generation.start", new { recordingId = id, template = await _host.MeetingMinutesAsync("server") });
        var done = await _host.FinishedAsync(start.GetProperty("jobId").GetString()!);
        Assert.Equal("done", done.GetProperty("stage").GetString());

        var output = _host.Events("generation.output");
        var requests = output.Where(e => e.GetProperty("kind").GetString() == "request").ToList();
        Assert.NotEmpty(requests);
        Assert.All(requests, r => Assert.True(r.GetProperty("streamed").GetBoolean()));
        foreach (var request in requests)
        {
            var pass = request.GetProperty("passId").GetString();
            var tokens = output.Where(e => e.GetProperty("passId").GetString() == pass && e.GetProperty("kind").GetString() == "token").Select(e => e.GetProperty("outputTokens").GetInt32()).ToList();
            Assert.NotEmpty(tokens);
            Assert.True(tokens[0] > 0);
            Assert.Equal(tokens.OrderBy(t => t), tokens);
            var reply = Assert.Single(output, e => e.GetProperty("passId").GetString() == pass && e.GetProperty("kind").GetString() == "reply");
            Assert.True(reply.GetProperty("outputTokens").GetInt32() > 0);
            Assert.True(reply.GetProperty("promptTokens").GetInt32() > 0);
        }

        var record = (await _host.ResultAsync("documents.get", new { recordingId = id, documentId = done.GetProperty("documentId").GetString() })).GetProperty("document").GetProperty("record");
        Assert.Equal(requests.Count, server.Requests.Count);
        Assert.True(record.GetProperty("stayedOnPc").GetBoolean());
    }

    [Fact]
    public async Task AServerOnTheNetworkIsRefusedWithExternalAiOffBeforeAnythingIsBuilt()
    {
        var id = await _host.CreateMeetingAsync();
        await ServerAsync(Network, "qwen3:8b");

        var error = await _host.ErrorAsync("generation.start", new { recordingId = id, template = await _host.MeetingMinutesAsync("server") });

        Assert.Equal(DomainErrorCodes.AiDisabled, error.GetProperty("code").GetString());
        Assert.StartsWith($"The server at {Network} is not on this PC, and external AI is off, so nothing was sent and no document was changed.", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal(0, _host.Providers.ServerCreated);
    }

    [Fact]
    public async Task AServerOnTheNetworkIsAskedAboutAndGetsOnlyWhatSettingsShares()
    {
        var id = await _host.CreateMeetingAsync();
        await _host.ResultAsync("settings.set", new { ai = new { enabled = true, share = new { participants = false } } });
        await ServerAsync(Network, "qwen3:8b");
        var template = await _host.MeetingMinutesAsync("server");

        var preview = await _host.ResultAsync("generation.preview", new { recordingId = id, template });
        Assert.False(preview.GetProperty("staysOnPc").GetBoolean());
        Assert.Equal("server", preview.GetProperty("provider").GetProperty("kind").GetString());
        Assert.Equal("qwen3:8b at 192.168.1.20:11434", preview.GetProperty("provider").GetProperty("modelLabel").GetString());
        Assert.Contains(preview.GetProperty("inputs").EnumerateArray(), i => i.GetProperty("key").GetString() == "participants" && !i.GetProperty("included").GetBoolean());

        var held = await _host.ResultAsync("generation.start", new { recordingId = id, template });
        Assert.True(held.GetProperty("confirmationRequired").GetBoolean());
        Assert.Equal("server", held.GetProperty("summary").GetProperty("providerId").GetString());
        Assert.Equal("Your own server", held.GetProperty("summary").GetProperty("providerName").GetString());
        Assert.False(held.GetProperty("summary").GetProperty("inputsUsed").GetProperty("participants").GetBoolean());
        Assert.Equal(0, _host.Providers.ServerCreated);

        // What the person confirmed is what is used, even if Settings changes before the job runs.
        await ServerAsync("http://192.168.1.99:11434", "qwen3:8b");
        await _host.ResultAsync("generation.confirm", new { jobId = held.GetProperty("jobId").GetString(), approved = true });
        var done = await _host.FinishedAsync(held.GetProperty("jobId").GetString()!);
        Assert.Equal("done", done.GetProperty("stage").GetString());
        Assert.Equal(Network, Assert.Single(_host.Providers.Server).Address.Text);
        var record = (await _host.ResultAsync("documents.get", new { recordingId = id, documentId = done.GetProperty("documentId").GetString() })).GetProperty("document").GetProperty("record");
        Assert.False(record.GetProperty("stayedOnPc").GetBoolean());
        Assert.Equal("qwen3:8b at 192.168.1.20:11434 (on your network)", record.GetProperty("modelLabel").GetString());
    }

    [Fact]
    public async Task AServerOnThisPcReadsWhatTheTemplateTicksAndThePreviewSaysItStays()
    {
        var id = await _host.CreateMeetingAsync();
        await _host.ResultAsync("settings.set", new { ai = new { share = new { participants = false } } });
        await ServerAsync("http://127.0.0.1:8080", "qwen/qwen3-8b");

        var preview = await _host.ResultAsync("generation.preview", new { recordingId = id, template = await _host.MeetingMinutesAsync("server") });

        Assert.True(preview.GetProperty("staysOnPc").GetBoolean());
        Assert.Equal("server", preview.GetProperty("providerId").GetString());
        Assert.Contains(preview.GetProperty("inputs").EnumerateArray(), i => i.GetProperty("key").GetString() == "participants" && i.GetProperty("included").GetBoolean());
    }

    [Fact]
    public async Task ATemplateMayNameTheServer()
    {
        var template = JsonNode.Parse((await _host.MeetingMinutesAsync("server")).GetRawText())!;
        template["id"] = string.Empty;
        template["name"] = "Minutes on my server";

        var saved = await _host.ResultAsync("templates.save", new { template });

        Assert.Equal("server", saved.GetProperty("providerId").GetString());
        var bad = JsonNode.Parse(template.ToJsonString())!;
        bad["providerId"] = "gemini";
        var error = await _host.ErrorAsync("templates.save", new { template = bad });
        Assert.Contains("Use anthropic, openai, server, local or null.", error.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TestConnectionFindsAServerOnThisPcWithExternalAiOff()
    {
        var server = new FakeOpenAiCompatibleServer(ServerPersonality.Ollama);
        _servers.Add(server);
        server.RequiredKey = "test-only-server-key";
        await _host.Get<ISecretStore>().SetKeyAsync(AiProviders.Server, "test-only-server-key", CancellationToken.None);

        var result = await _host.ResultAsync("ai.serverProbe", new { address = server.Address + "/v1" });

        Assert.Equal(
            $$"""{"reachable":true,"serverName":"Ollama 0.12.3","code":null,"detail":"Connected to Ollama 0.12.3 at {{server.Address}}: 2 models. It runs on this PC, so nothing leaves it.","loopback":true,"leavesPc":false,"insecure":false,"models":["llama3.2:3b","qwen3:8b"]}""",
            result.GetRawText());
        Assert.All(server.Requests, r => Assert.Equal("Bearer test-only-server-key", r.Header("Authorization")));
        Assert.Equal(["/v1/models", "/api/version"], server.Requests.Select(r => r.Path));
        Assert.Null(_host.Host.Settings.Current.Ai.Server.Address); // Testing saves nothing.
    }

    [Theory]
    [InlineData(Network, false, "ai.disabled", "external AI is off, so Memento did not contact it")]
    [InlineData("http://203.0.113.10:8000", true, "ai.serverInsecure", "only over https, so it did not contact it")]
    public async Task TestConnectionDoesNotContactAnAddressItMayNotSendTo(string address, bool enabled, string code, string expected)
    {
        await _host.ResultAsync("settings.set", new { ai = new { enabled } });

        var result = await _host.ResultAsync("ai.serverProbe", new { address });

        Assert.False(result.GetProperty("reachable").GetBoolean());
        Assert.Equal(code, result.GetProperty("code").GetString());
        Assert.Contains(expected, result.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.True(result.GetProperty("leavesPc").GetBoolean());
        Assert.Empty(result.GetProperty("models").EnumerateArray());
    }

    [Fact]
    public async Task TestConnectionRefusesSomethingThatIsNotAnAddress()
    {
        var error = await _host.ErrorAsync("ai.serverProbe", new { address = "ftp://localhost" });

        Assert.Equal(DomainErrorCodes.SettingsInvalidValue, error.GetProperty("code").GetString());
        Assert.Equal("address", error.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task TestConnectionSaysWhenNothingIsListening()
    {
        var port = FakeOpenAiCompatibleServer.ClosedPort();

        var result = await _host.ResultAsync("ai.serverProbe", new { address = string.Create(CultureInfo.InvariantCulture, $"localhost:{port}") });

        Assert.Equal(DomainErrorCodes.AiServerUnreachable, result.GetProperty("code").GetString());
        Assert.Equal(string.Create(CultureInfo.InvariantCulture, $"Nothing is listening at http://localhost:{port}. Start Ollama or LM Studio, or check the address in Settings › AI and privacy."), result.GetProperty("detail").GetString());
    }

    private Task<JsonElement> ServerAsync(string address, string? model, int contextTokens = 8192) =>
        _host.ResultAsync("settings.set", new { ai = new { providers = new { server = new { address, model, contextTokens } } } });

    private async Task<JsonElement> InfoAsync() =>
        (await _host.ResultAsync("providers.list", new { })).GetProperty("providers").EnumerateArray().Single(p => p.GetProperty("id").GetString() == "server");

    private async Task<(bool Ready, string? Code, string? Reason)> StatusAsync()
    {
        var info = await InfoAsync();
        return (info.GetProperty("ready").GetBoolean(), info.GetProperty("code").GetString(), info.GetProperty("reason").GetString());
    }
}
