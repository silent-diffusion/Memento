namespace Memento.AI.Local;

/// <summary>
/// A line from the host to the worker, mirroring Core's worker protocol: <c>{"type":"start","job":{"kind":"llm","llm":{…}}}</c>
/// once, then optionally <c>{"type":"cancel"}</c>. The integration adds <c>Llm</c> to Core's <c>WorkerJob</c>.
/// </summary>
public sealed record LocalLlmWorkerCommand(string Type, LocalLlmWorkerJob? Job = null);
