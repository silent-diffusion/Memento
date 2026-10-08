using Memento.AI.Anthropic;
using Memento.AI.Http;
using Memento.AI.Tests.Fakes;

namespace Memento.AI.Tests.Cloud;

/// <summary>
/// Security audit 2026-10-07, item 5: with AI on, the production HTTP client sends every request of a generation to
/// the chosen provider and nowhere else (no telemetry, no other host), shown with the provider pointed at a local fake.
/// </summary>
[Collection(nameof(CloudDestinationGroup))]
public sealed class CloudDestinationTests : IDisposable
{
    private readonly ScriptedHttpServer _server = new();
    private readonly AiHttpClient _http = new();

    public void Dispose()
    {
        _http.Dispose();
        _server.Dispose();
    }

    [Fact]
    public async Task EveryRequestGoesToTheChosenProviderOnly()
    {
        for (var i = 0; i < 3; i++)
        {
            _server.Enqueue(AnthropicStreams.Text(["{\"items\":[]}"]));
        }

        var provider = new AnthropicProvider(
            _http.Client,
            new FakeSecrets().With("anthropic", "test-only-destination-key-0001"),
            new AnthropicOptions { BaseUrl = _server.BaseUrl },
            new SpyLogger<AnthropicProvider>(),
            new InstantTimeProvider());
        using var spy = new NetworkSpy();

        foreach (var purpose in new[] { "map.commitments#1", "map.points#1", "verify.batch" })
        {
            await provider.GenerateAsync(AiRequest.Create(purpose, "Answer in JSON.", "[1] Speaker 1: We agreed to ship on Friday."), null, CancellationToken.None);
        }

        var http = spy.Events.Where(e => e.StartsWith("http ", StringComparison.Ordinal)).ToList();
        var connects = spy.Events.Where(e => e.StartsWith("connect ", StringComparison.Ordinal)).ToList();
        Assert.Equal(3, http.Count);
        Assert.All(http, e => Assert.StartsWith("http POST " + _server.BaseUrl + "v1/messages", e, StringComparison.Ordinal));
        Assert.NotEmpty(connects);
        Assert.All(connects, e => Assert.Equal("connect 127.0.0.1:" + _server.Port.ToString(System.Globalization.CultureInfo.InvariantCulture), e));
        Assert.Equal(3, _server.Requests.Count);
    }
}
