namespace Memento.Core.Projects;

/// <summary>What recovery at launch found (DESIGN.md §17 recovery dialog).</summary>
/// <param name="MayBeMissingMs">Upper bound of audio lost after the last checkpoint.</param>
/// <param name="Acknowledged">The user dismissed the dialog for this project.</param>
/// <param name="VideoTracks">2.0: how much of each video track came back (a torn last fragment cut off); <c>null</c> or empty without video.</param>
public sealed record ProjectRecovery(
    DateTimeOffset RecoveredAt,
    DateTimeOffset? LastCheckpointAt,
    int TracksIntact,
    int TracksTotal,
    long RecoveredDurationMs,
    long MayBeMissingMs,
    bool Acknowledged,
    IReadOnlyList<Bridge.Contracts.RecoveredVideoTrack>? VideoTracks = null);
