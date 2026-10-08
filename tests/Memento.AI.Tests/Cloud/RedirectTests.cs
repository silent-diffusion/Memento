using System.Globalization;
using Memento.AI.Anthropic;
using Memento.AI.Http;
using Memento.AI.OpenAI;
using Memento.AI.Tests.Fakes;

namespace Memento.AI.Tests.Cloud;

/// <summary>The shared client follows no redirect, so neither the key nor the transcript reaches another address.</summary>
public sealed class RedirectTests : IDisposable
{
    private const string AnthropicKey = "test-only-anthropic-key-5d1e7a0c3f";
    private const string OpenAiKey = "test-only-openai-key-9b2f4c6a8e";

    private readonly ScriptedHttpServer _provider = new();
    private readonly ScriptedHttpServer _elsewhere = new();
    private readonly AiHttpClient _http = new();
    private readonly InstantTimeProvider _time = new();

    public void Dispose()
    {
        _http.Dispose();
        _provider.Dispose();
        _elsewhere.Dispose();
    }

    [Theory]
    [InlineData(302)]
    [InlineData(307)]
    [InlineData(308)]
    public async Task AnthropicDoesNotFollowARedirectWithTheKeyOrTheText(int status)
    {
        _provider.Enqueue(Redirect(status));
        _elsewhere.Enqueue(AnthropicStreams.Text(["captured"]));
        var provider = new AnthropicProvider(_http.Client, new FakeSecrets().With("anthropic", AnthropicKey), new AnthropicOptions { BaseUrl = _provider.BaseUrl }, new SpyLogger<AnthropicProvider>(), _time);

        var error = await Assert.ThrowsAsync<AiException>(() => provider.GenerateAsync(AiRequest.Create("test.redirect", "Be brief.", "The confidential transcript."), null, CancellationToken.None));

        Assert.Equal(AiErrorCodes.ProviderError, error.Code);
        Assert.Equal(status, error.Error.HttpStatus);
        Assert.Contains("Memento does not follow redirects, so nothing was sent there", error.Message, StringComparison.Ordinal);
        Assert.Contains("localhost", error.Message, StringComparison.Ordinal);
        Assert.Empty(_elsewhere.Requests);
        Assert.Single(_provider.Requests); // Not retried either.
        Assert.DoesNotContain(AnthropicKey, error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenAiDoesNotFollowARedirectWithTheKeyOrTheText()
    {
        _provider.Enqueue(Redirect(307));
        _elsewhere.Enqueue(OpenAiStreams.Text(["captured"]));
        var provider = new OpenAiProvider(_http.Client, new FakeSecrets().With("openai", OpenAiKey), new OpenAiOptions { BaseUrl = _provider.BaseUrl }, new SpyLogger<OpenAiProvider>(), _time);

        var error = await Assert.ThrowsAsync<AiException>(() => provider.GenerateAsync(AiRequest.Create("test.redirect", "Be brief.", "The confidential transcript."), null, CancellationToken.None));

        Assert.Equal(AiErrorCodes.ProviderError, error.Code);
        Assert.Contains("does not follow redirects", error.Message, StringComparison.Ordinal);
        Assert.Empty(_elsewhere.Requests);
        Assert.DoesNotContain(OpenAiKey, error.ToString(), StringComparison.Ordinal);
    }

    private ScriptedResponse Redirect(int status) =>
        ScriptedResponse.Json(status, "{}", ("Location", string.Create(CultureInfo.InvariantCulture, $"http://localhost:{_elsewhere.Port}/v1/messages")));
}
