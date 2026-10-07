namespace Memento.AI.Local;

/// <summary>The answer to one <see cref="LocalLlmPrompt"/>.</summary>
/// <param name="Index">The prompt's position in the job.</param>
/// <param name="StopReason">One of <see cref="LocalLlmStopReasons"/>.</param>
public sealed record LocalLlmOutput(
    int Index,
    string Text,
    string StopReason,
    int PromptTokens,
    int OutputTokens,
    double PromptMs,
    double GenerateMs)
{
    /// <summary>Output tokens per second, not counting the first (which waits for the prompt).</summary>
    public double TokensPerSecond => OutputTokens > 1 && GenerateMs > 0 ? (OutputTokens - 1) / (GenerateMs / 1000) : 0;

    public double PromptTokensPerSecond => PromptMs > 0 ? PromptTokens / (PromptMs / 1000) : 0;
}
