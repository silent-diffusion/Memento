namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>recording.current</c>: the active (or finalizing) session, or <c>null</c>.</summary>
public sealed record RecordingCurrentResult(RecordingStatePayload? Session);
