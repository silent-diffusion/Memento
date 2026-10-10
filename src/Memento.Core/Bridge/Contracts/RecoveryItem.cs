namespace Memento.Core.Bridge.Contracts;

/// <summary>One recovered recording for the recovery dialog (DESIGN.md §17).</summary>
/// <param name="MayBeMissingMs">At most this much audio after the last checkpoint may be missing; 0 when nothing can be.</param>
/// <param name="VideoTracksIntact">2.0: video tracks that kept their frames (the dialog names video when <paramref name="VideoTracksTotal"/> is above 0).</param>
/// <param name="VideoTracksTotal">2.0: video tracks that were being recorded; 0 for an audio recording.</param>
public sealed record RecoveryItem(
    string RecordingId,
    string Title,
    DateTimeOffset StartedAt,
    int TracksIntact,
    int TracksTotal,
    DateTimeOffset? LastCheckpointAt,
    long RecoveredDurationMs,
    long MayBeMissingMs,
    int VideoTracksIntact = 0,
    int VideoTracksTotal = 0);
