namespace Memento.AI.Local;

/// <summary>The job in a <c>start</c> command: <see cref="Kind"/> is <see cref="LlmKind"/> and <see cref="Llm"/> the body.</summary>
public sealed record LocalLlmWorkerJob(string Kind, LocalLlmJob? Llm = null)
{
    public const string LlmKind = "llm";
}
