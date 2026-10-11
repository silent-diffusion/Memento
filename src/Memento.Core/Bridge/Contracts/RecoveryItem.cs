namespace Memento.Core.Bridge.Contracts;

/// <summary>One recovered recording for the recovery dialog (DESIGN.md §17).</summary>
/// <param name="MayBeMissingMs">At most this much audio after the last checkpoint may be missing; 0 when nothing can be.</param>
/// <param name="VideoTracks">2.0: how much of each video track came back; <c>[]</c> without video.</param>
public sealed record RecoveryItem(
    string RecordingId,
    string Title,
    DateTimeOffset StartedAt,
    int TracksIntact,
    int TracksTotal,
    DateTimeOffset? LastCheckpointAt,
    long RecoveredDurationMs,
    long MayBeMissingMs,
    IReadOnlyList<RecoveredVideoTrack>? VideoTracks = null)
{
    /// <summary>Never null on the wire.</summary>
    public IReadOnlyList<RecoveredVideoTrack> VideoTracks { get; init; } = VideoTracks ?? [];
}
