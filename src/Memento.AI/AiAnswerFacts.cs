namespace Memento.AI;

/// <summary>How one answer of a local batch ended, reported as soon as it is complete (<see cref="AiProgressStage.Answered"/>).</summary>
/// <param name="StopReason">The provider's own stop reason (<c>eog</c>, <c>max_tokens</c>, <c>context</c>).</param>
/// <param name="PromptTokens">The tokens of the prompt the model read.</param>
/// <param name="TokensPerSecond">Output tokens per second, not counting the first.</param>
public sealed record AiAnswerFacts(string StopReason, int PromptTokens, double TokensPerSecond);
