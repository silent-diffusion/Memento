namespace Memento.Core.Projects;

/// <summary>What recovery at launch found (DESIGN.md §17 recovery dialog).</summary>
/// <param name="MayBeMissingMs">Upper bound of audio lost after the last checkpoint.</param>
/// <param name="Acknowledged">The user dismissed the dialog for this project.</param>
public sealed record ProjectRecovery(
    DateTimeOffset RecoveredAt,
    DateTimeOffset? LastCheckpointAt,
    int TracksIntact,
    int TracksTotal,
    long RecoveredDurationMs,
    long MayBeMissingMs,
    bool Acknowledged);
