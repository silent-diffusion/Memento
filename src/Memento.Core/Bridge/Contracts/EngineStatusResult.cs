namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>engine.status</c>.</summary>
public sealed record EngineStatusResult(EngineStatusDetail Transcription, EngineStatusDetail Speakers);
