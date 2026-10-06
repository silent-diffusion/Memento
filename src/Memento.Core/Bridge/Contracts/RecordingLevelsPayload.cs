namespace Memento.Core.Bridge.Contracts;

/// <summary>Payload of <c>recording.levels</c>, at most 30 per second.</summary>
public sealed record RecordingLevelsPayload(string SessionId, IReadOnlyList<SourceLevel> Levels);
