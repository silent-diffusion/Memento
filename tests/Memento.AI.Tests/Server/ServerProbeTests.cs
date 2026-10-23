using System.Globalization;
using Memento.AI.Http;
using Memento.AI.Server;
using Memento.AI.Tests.Fakes;
using Memento.Core.Ai;

namespace Memento.AI.Tests.Server;

/// <summary>"Test connection" (<c>ai.serverProbe</c>) against fake servers: who answered, the models, and each failure. Only the model list (and Ollama's version) is asked for.</summary>
public sealed class ServerProbeTests : IDisposable
{
    private readonly AiHttpClient _http = new();
    private readonly SpyLogger<ServerProbe> _log = new();
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
    public async Task OllamaIsNamedWithItsVersionAndListsItsModelsSorted()
    {
        var server = Fake(ServerPersonality.Ollama);

        var outcome = await Probe().ProbeAsync(Address(server), null, CancellationToken.None);

        Assert.True(outcome.Reachable);
        Assert.Null(outcome.Code);
        Assert.Equal("Ollama 0.12.3", outcome.ServerName);
        Assert.Equal(["llama3.2:3b", "qwen3:8b"], outcome.Models);
        Assert.Equal($"Connected to Ollama 0.12.3 at {server.Address}: 2 models. It runs on this PC, so nothing leaves it.", outcome.Detail);
        Assert.Equal([("GET", "/v1/models"), ("GET", "/api/version")], server.Requests.Select(r => (r.Method, r.Path)));
    }

    [Theory]
    [InlineData(ServerPersonality.LmStudio, "LM Studio")]
    [InlineData(ServerPersonality.Vllm, "vLLM")]
    public async Task OtherServersAreNamedFromTheirModelList(ServerPersonality personality, string name)
    {
        var server = Fake(personality);

        var outcome = await Probe().ProbeAsync(Address(server), null, CancellationToken.None);

        Assert.Equal(name, outcome.ServerName);
        Assert.Single(outcome.Models);
        Assert.Equal([("GET", "/v1/models")], server.Requests.Select(r => (r.Method, r.Path)));
    }

    [Fact]
    public async Task AServerThatDoesNotSayWhoItIsIsNamedByItsHost()
    {
        var server = Fake(ServerPersonality.LmStudio);
        server.Override = r => r.Path == "/v1/models" ? ScriptedResponse.Json(200, """{"object":"list","data":[{"id":"b-model","object":"model"},{"id":"A-model","object":"model"}]}""") : null;

        var outcome = await Probe().ProbeAsync(Address(server), null, CancellationToken.None);

        Assert.Equal(string.Create(CultureInfo.InvariantCulture, $"127.0.0.1:{server.Port}"), outcome.ServerName);
        Assert.Equal(["A-model", "b-model"], outcome.Models);
    }

    [Fact]
    public async Task NothingListeningIsTheBriefsSentence()
    {
        var port = FakeOpenAiCompatibleServer.ClosedPort();
        var address = ServerAddress.Parse(string.Create(CultureInfo.InvariantCulture, $"http://localhost:{port}"), out _)!;

        var outcome = await Probe().ProbeAsync(address, null, CancellationToken.None);

        Assert.False(outcome.Reachable);
        Assert.Equal(AiErrorCodes.ServerUnreachable, outcome.Code);
        Assert.Equal($"Nothing is listening at http://localhost:{port}. Start Ollama or LM Studio, or check the address in Settings › AI and privacy.", outcome.Detail);
    }

    [Fact]
    public async Task AServerThatAsksForAKeyGetsTheSavedOne()
    {
        var server = Fake(ServerPersonality.Vllm);
        server.RequiredKey = "test-only-probe-key";

        var without = await Probe().ProbeAsync(Address(server), null, CancellationToken.None);
        var with = await Probe().ProbeAsync(Address(server), "test-only-probe-key", CancellationToken.None);

        Assert.Equal(AiErrorCodes.ServerNeedsKey, without.Code);
        Assert.True(without.Reachable);
        Assert.Equal($"The server at {server.Address} asks for a key (HTTP 401). Add the server's key in Settings › AI and privacy, then test again.", without.Detail);
        Assert.Null(with.Code);
        Assert.Equal("Bearer test-only-probe-key", server.Requests[^1].Header("Authorization"));
        Assert.DoesNotContain("test-only-probe-key", _log.All, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARedirectIsNotFollowed()
    {
        var server = Fake(ServerPersonality.Ollama);
        var elsewhere = Fake(ServerPersonality.Ollama);
        server.Override = _ => ScriptedResponse.Json(307, "{}", ("Location", elsewhere.Address + "/v1/models"));

        var outcome = await Probe().ProbeAsync(Address(server), "test-only-probe-key", CancellationToken.None);

        Assert.Equal(AiErrorCodes.ServerNotCompatible, outcome.Code);
        Assert.Contains("answered with a redirect to http://127.0.0.1:", outcome.Detail, StringComparison.Ordinal);
        Assert.Contains("Memento does not follow redirects", outcome.Detail, StringComparison.Ordinal);
        Assert.Empty(elsewhere.Requests);
    }

    [Fact]
    public async Task AnAddressThatIsNotAnApiIsNotCompatible()
    {
        var server = Fake(ServerPersonality.Ollama);
        server.Override = _ => new ScriptedResponse { Status = 200, ContentType = "text/html", Parts = [("<html>router admin</html>", TimeSpan.Zero)] };

        var outcome = await Probe().ProbeAsync(Address(server), null, CancellationToken.None);

        Assert.True(outcome.Reachable);
        Assert.Equal(AiErrorCodes.ServerNotCompatible, outcome.Code);
        Assert.Equal($"The server at {server.Address} does not answer like an OpenAI-compatible server (no model list Memento could read at /v1/models). Check the address in Settings › AI and privacy: Memento adds /v1/models to it.", outcome.Detail);
    }

    [Fact]
    public async Task AServerWithoutModelsSaysSo()
    {
        var server = Fake(ServerPersonality.Ollama);
        server.Models.Clear();

        var outcome = await Probe().ProbeAsync(Address(server), null, CancellationToken.None);

        Assert.Equal(ServerProbe.NoModelsCode, outcome.Code);
        Assert.Empty(outcome.Models);
        // Without a model there is no owned_by to say which server it is, so the host names it.
        Assert.Equal($"127.0.0.1:{server.Port} answers at {server.Address} but offers no model. Download or load one on the server (for Ollama, run ollama pull with a model name), then test again.", outcome.Detail);
    }

    [Fact]
    public async Task AServerThatNeverAnswersTimesOutOnTheGivenClock()
    {
        // The timer fires as soon as it is set, so the stall is met by the probe's own limit, not by waiting for it.
        var server = Fake(ServerPersonality.Ollama);
        server.Override = _ => ScriptedResponse.Stall();
        var time = new InstantTimeProvider();

        var outcome = await new ServerProbe(_http.Client, time, _log).ProbeAsync(Address(server), null, CancellationToken.None);

        Assert.False(outcome.Reachable);
        Assert.Equal(AiErrorCodes.ServerUnreachable, outcome.Code);
        Assert.Equal($"The server at {server.Address} did not answer within 15 seconds. Check that it is running and not busy loading a model, then test again.", outcome.Detail);
        Assert.Contains(ServerProbe.Timeout, time.Delays);
    }

    private ServerProbe Probe() => new(_http.Client, TimeProvider.System, _log);

    private static ServerAddress Address(FakeOpenAiCompatibleServer server) => ServerAddress.Parse(server.Address, out _)!;

    private FakeOpenAiCompatibleServer Fake(ServerPersonality personality)
    {
        var server = new FakeOpenAiCompatibleServer(personality);
        _servers.Add(server);
        return server;
    }
}
