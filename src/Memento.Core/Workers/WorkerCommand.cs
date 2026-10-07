namespace Memento.Core.Workers;

/// <summary>A line from the host to the worker: <c>{"type":"start","job":{…}}</c> once, then optionally <c>{"type":"cancel"}</c>.</summary>
public sealed record WorkerCommand(string Type, WorkerJob? Job = null);
