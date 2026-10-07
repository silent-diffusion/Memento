namespace Memento.Core.Bridge.Contracts;

/// <summary>When a segment was last edited and the text the engine produced before the first edit.</summary>
public sealed record TranscriptEdit(DateTimeOffset At, string Original);
