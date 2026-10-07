namespace Memento.AI;

/// <summary>How long a request took.</summary>
/// <param name="Total">From the call to the last byte, including retries and waiting for a rate limit.</param>
/// <param name="FirstToken">From the successful attempt's start to the first output text, when streamed.</param>
/// <param name="PromptEvaluation">Local only: reading the prompt.</param>
/// <param name="Generation">From the first to the last output token.</param>
/// <param name="Attempts">HTTP attempts made (1 when nothing was retried).</param>
/// <param name="ModelLoad">Local only: loading the weights and creating the context, when this request did it.</param>
public sealed record AiTimings(
    TimeSpan Total,
    TimeSpan? FirstToken = null,
    TimeSpan? PromptEvaluation = null,
    TimeSpan? Generation = null,
    int Attempts = 1,
    TimeSpan? ModelLoad = null)
{
    /// <summary>Output tokens per second during generation, when it can be computed.</summary>
    public double? OutputTokensPerSecond(int outputTokens) =>
        Generation is { TotalSeconds: > 0 } g && outputTokens > 1 ? (outputTokens - 1) / g.TotalSeconds : null;
}
