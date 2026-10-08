using System.Net;
using Memento.AI.Anthropic;
using Memento.AI.Http;
using Memento.AI.Tests.Fakes;

namespace Memento.AI.Tests.Cloud;

/// <summary>The handler seam shows where a provider request goes without touching the network.</summary>
public sealed class AiHttpClientTests
{
    [Fact]
    public async Task AClaudeRequestGoesOnlyToTheAnthropicApi()
    {
        var handler = new RecordingHttpHandler(HttpStatusCode.Unauthorized);
        using var http = new AiHttpClient(handler);
        var provider = new AnthropicProvider(http.Client, new FakeSecrets().With("anthropic", "test-only-anthropic-key-3c8e1f5a7b"), new AnthropicOptions(), new SpyLogger<AnthropicProvider>(), new InstantTimeProvider());

        var error = await Assert.ThrowsAsync<AiException>(() => provider.GenerateAsync(AiRequest.Create("test.seam", "Be brief.", "Hi"), null, CancellationToken.None));

        Assert.Equal(AiErrorCodes.InvalidKey, error.Code);
        var (uri, headers) = Assert.Single(handler.Requests);
        Assert.Equal(new Uri("https://api.anthropic.com/v1/messages"), uri);
        Assert.Contains("x-api-key", headers, StringComparer.OrdinalIgnoreCase);
    }
}
