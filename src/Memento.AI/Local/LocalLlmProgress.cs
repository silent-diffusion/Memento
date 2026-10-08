namespace Memento.AI.Local;

/// <summary>A progress line of a local job. <see cref="Delta"/> is content: shown, never logged.</summary>
/// <param name="Phase">One of <c>loading</c>, <c>warmup</c>, <c>prompt</c>, <c>generating</c>, <c>answered</c>.</param>
/// <param name="PromptIndex">The prompt being worked on (-1 while loading).</param>
/// <param name="Delta">With <c>generating</c>: the text decoded since the previous line (coalesced, <see cref="LocalTokenCoalescer"/>).</param>
/// <param name="OutputTokens">With <c>generating</c>: pieces decoded so far; with <c>answered</c>: the answer's output tokens.</param>
public sealed record LocalLlmProgress(string Phase, int PromptIndex, int PromptCount, string? Delta = null, int OutputTokens = 0)
{
    public const string Loading = "loading";
    public const string WarmingUp = "warmup";
    public const string ReadingPrompt = "prompt";
    public const string Generating = "generating";

    /// <summary>One prompt's answer is complete (its text is the <c>generating</c> deltas; the batch line follows later).</summary>
    public const string Answered = "answered";

    /// <summary>With <c>generating</c>: milliseconds since the first piece; with <c>answered</c>: the time spent writing.</summary>
    public double? ElapsedMs { get; init; }

    /// <summary>With <c>answered</c>: one of <see cref="LocalLlmStopReasons"/>.</summary>
    public string? StopReason { get; init; }

    /// <summary>With <c>answered</c>: the prompt's tokens.</summary>
    public int? PromptTokens { get; init; }

    /// <summary>With <c>answered</c>: output tokens per second, as <see cref="LocalLlmOutput.TokensPerSecond"/>.</summary>
    public double? TokensPerSecond { get; init; }
}
