namespace Memento.Audio.Recording;

/// <summary>State after a checkpoint; everything listed is on disk with patched headers (feeds <c>recording.state.json</c>).</summary>
public sealed record SessionCheckpoint(DateTimeOffset At, TimeSpan Elapsed, IReadOnlyList<TrackCheckpointInfo> Tracks);
