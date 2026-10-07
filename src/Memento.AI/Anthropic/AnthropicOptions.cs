using Memento.AI.Http;

namespace Memento.AI.Anthropic;

/// <summary>Settings for <see cref="AnthropicProvider"/>; the defaults are the production values.</summary>
public sealed record AnthropicOptions
{
    public static Uri DefaultBaseUrl { get; } = new("https://api.anthropic.com/");

    /// <summary>The API root; tests point it at a local fake server. Must end with a slash.</summary>
    public Uri BaseUrl { get; init; } = DefaultBaseUrl;

    /// <summary>The most capable generally available Claude model (claude-api skill, September 2026).</summary>
    public string Model { get; init; } = "claude-opus-5-5";

    /// <summary>
    /// <c>output_config.effort</c>: <c>low</c>, <c>medium</c>, <c>high</c>, <c>xhigh</c>, <c>max</c>, or <c>null</c> for
    /// the model default (medium on Claude Opus 5.5). Grounded extraction and verification are accuracy work, so high.
    /// </summary>
    public string? Effort { get; init; } = "high";

    /// <summary>
    /// Send <c>fallbacks: "default"</c> with the <c>server-side-fallback-2026-07-01</c> beta, so a request a safety
    /// classifier declines is re-run server-side on Anthropic's recommended fallback model instead of failing.
    /// </summary>
    public bool UseServerSideFallback { get; init; } = true;

    /// <summary>Claude Opus 5.5 rejects <c>temperature</c>; enable only for a model that accepts it.</summary>
    public bool SendTemperature { get; init; }

    public int ContextTokens { get; init; } = 1_000_000;

    public int MaxOutputTokens { get; init; } = 128_000;

    public CloudHttpOptions Http { get; init; } = new();
}
