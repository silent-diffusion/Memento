namespace Memento.AI.Local;

/// <summary>A progress line of a local job. <see cref="Delta"/> is content: shown, never logged.</summary>
/// <param name="Phase">One of <c>loading</c>, <c>warmup</c>, <c>prompt</c>, <c>generating</c>.</param>
/// <param name="PromptIndex">The prompt being worked on (-1 while loading).</param>
public sealed record LocalLlmProgress(string Phase, int PromptIndex, int PromptCount, string? Delta = null, int OutputTokens = 0)
{
    public const string Loading = "loading";
    public const string WarmingUp = "warmup";
    public const string ReadingPrompt = "prompt";
    public const string Generating = "generating";
}
