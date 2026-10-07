namespace Memento.AI;

/// <summary>Tokens a request used, as the provider reports them (local: counted by the engine).</summary>
/// <param name="CachedInputTokens">Input tokens read from the provider's prompt cache, when reported.</param>
public sealed record AiUsage(int InputTokens, int OutputTokens, int? CachedInputTokens = null)
{
    public static AiUsage None { get; } = new(0, 0);
}
