namespace Memento.Core.Projects;

/// <summary>What recovery at launch found (DESIGN.md §17 recovery dialog).</summary>
/// <param name="MayBeMissingMs">Upper bound of audio lost after the last checkpoint.</param>
/// <param name="Acknowledged">The user dismissed the dialog for this project.</param>
/// <param name="VideoTracksIntact">2.0: video tracks with frames kept (a torn last fragment cut off).</param>
/// <param name="VideoTracksTotal">2.0: video tracks that were being recorded.</param>
public sealed record ProjectRecovery(
    DateTimeOffset RecoveredAt,
    DateTimeOffset? LastCheckpointAt,
    int TracksIntact,
    int TracksTotal,
    long RecoveredDurationMs,
    long MayBeMissingMs,
    bool Acknowledged,
    int VideoTracksIntact = 0,
    int VideoTracksTotal = 0);
