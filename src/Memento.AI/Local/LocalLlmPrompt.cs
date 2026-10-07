namespace Memento.AI.Local;

/// <summary>One generation inside a <see cref="LocalLlmJob"/>.</summary>
/// <param name="Purpose">The request's purpose tag (logged; never content).</param>
/// <param name="Grammar">GBNF with a <c>root</c> rule, or <c>null</c> for free text.</param>
/// <param name="Temperature">0 is greedy (top-k 1), the pipeline default.</param>
public sealed record LocalLlmPrompt(
    string Purpose,
    string System,
    IReadOnlyList<LocalLlmTurn> Messages,
    int MaxTokens,
    string? Grammar = null,
    double Temperature = 0);
