using System.Text.Json;

namespace Memento.Core.Workers;

/// <summary>
/// A line from the host to the worker: <c>{"type":"start","job":{…}}</c> once, then optionally <c>{"type":"cancel"}</c>;
/// a local model job that stays loaded also takes <c>{"type":"prompts","llm":{…}}</c> and <c>{"type":"end"}</c>.
/// </summary>
/// <param name="Llm">With <c>prompts</c>: Memento.AI's prompt batch as raw JSON.</param>
public sealed record WorkerCommand(string Type, WorkerJob? Job = null, JsonElement? Llm = null);
