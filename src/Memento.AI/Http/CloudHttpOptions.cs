namespace Memento.AI.Http;

/// <summary>Retry and timeout settings shared by the cloud providers.</summary>
public sealed record CloudHttpOptions
{
    /// <summary>
    /// Attempts per request, including the first. Only answers that say "not processed" are retried: HTTP 429 (not
    /// quota exhaustion) and 500, 502, 503, 504, 529, or an overload error before any text arrived. A timeout or a
    /// broken connection is never retried, so a request the provider may have processed is not sent twice.
    /// </summary>
    public int MaxAttempts { get; init; } = 3;

    /// <summary>Backoff before the second attempt when the provider gives no <c>retry-after</c>; doubles each time.</summary>
    public TimeSpan BaseRetryDelay { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan MaxRetryDelay { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>A <c>retry-after</c> longer than this is not waited for: the request fails with <see cref="AiErrorCodes.RateLimited"/>.</summary>
    public TimeSpan MaxRetryAfter { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>From sending to the response headers.</summary>
    public TimeSpan ResponseTimeout { get; init; } = TimeSpan.FromSeconds(90);

    /// <summary>The longest gap between two stream lines (providers send keep-alive pings while thinking).</summary>
    public TimeSpan StreamIdleTimeout { get; init; } = TimeSpan.FromSeconds(120);

    /// <summary>The whole request, all attempts and waits included.</summary>
    public TimeSpan TotalTimeout { get; init; } = TimeSpan.FromMinutes(20);

    /// <summary>
    /// Added to <see cref="AiRequest.MaxOutputTokens"/> for the provider's own limit, because current reasoning models
    /// count their thinking against it; the visible answer limit stays what the request asked for.
    /// </summary>
    public int ThinkingHeadroomTokens { get; init; } = 16_000;
}
