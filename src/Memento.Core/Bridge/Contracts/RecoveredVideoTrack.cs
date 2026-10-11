namespace Memento.Core.Bridge.Contracts;

/// <summary>One video track in a <c>recovery.list</c> item: how much of it came back.</summary>
/// <param name="TrackId"><c>screen</c> or <c>camera</c>.</param>
public sealed record RecoveredVideoTrack(string TrackId, string SourceName, long RecoveredDurationMs);
