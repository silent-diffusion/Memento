namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>recording.start</c>.</summary>
public sealed record RecordingStartResult(string SessionId, string RecordingId, DateTimeOffset StartedAt);
