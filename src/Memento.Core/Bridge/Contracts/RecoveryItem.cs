namespace Memento.Core.Bridge.Contracts;

/// <summary>One recovered recording for the recovery dialog (DESIGN.md §17).</summary>
/// <param name="MayBeMissingMs">At most this much audio after the last checkpoint may be missing; 0 when nothing can be.</param>
public sealed record RecoveryItem(
    string RecordingId,
    string Title,
    DateTimeOffset StartedAt,
    int TracksIntact,
    int TracksTotal,
    DateTimeOffset? LastCheckpointAt,
    long RecoveredDurationMs,
    long MayBeMissingMs);
