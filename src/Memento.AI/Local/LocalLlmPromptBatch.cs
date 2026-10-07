namespace Memento.AI.Local;

/// <summary>The body of a <c>prompts</c> line: the next prompts for a local model job that stays loaded (<see cref="LocalLlmJob.Session"/>).</summary>
public sealed record LocalLlmPromptBatch(IReadOnlyList<LocalLlmPrompt> Prompts);
