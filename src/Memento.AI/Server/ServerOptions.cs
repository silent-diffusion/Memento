using Memento.AI.Http;
using Memento.Core.Ai;
using Memento.Core.Settings;

namespace Memento.AI.Server;

/// <summary>Settings for <see cref="ServerProvider"/>: where the server is, the model to ask for and the context it runs.</summary>
/// <param name="Address">The parsed address (<see cref="ServerAddress"/>); its zone decides what the sentences say is safe.</param>
/// <param name="Model">The model as the server names it (<c>qwen3:8b</c>).</param>
/// <param name="ContextTokens">The context the server runs the model with (Settings; Ollama's default is 4,096 unless <c>num_ctx</c> was raised).</param>
public sealed record ServerOptions(ServerAddress Address, string Model, int ContextTokens = AiServerSettings.DefaultContextTokens)
{
    /// <summary>
    /// Timeouts and retries. A server on this PC may load the model on the first request, which takes minutes for a large
    /// one on a slow disk, so the wait for the response headers is longer than the clouds'; a retry happens only for an
    /// answer that says the request was not processed (429, 500, 502, 503, 504), as for the clouds.
    /// </summary>
    public CloudHttpOptions Http { get; init; } = new()
    {
        ResponseTimeout = TimeSpan.FromMinutes(5),
        StreamIdleTimeout = TimeSpan.FromMinutes(3),
        MaxAttempts = 2,
        ThinkingHeadroomTokens = 0,
    };
}
