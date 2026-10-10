namespace Memento.Core.Bridge.Contracts;

/// <summary>Payload of <c>recording.state</c>, sent on every change and at least every second while recording.</summary>
/// <param name="State"><c>recording</c>, <c>paused</c>, <c>finalizing</c>, <c>ready</c> or <c>stopped</c>.</param>
/// <param name="ElapsedMs">Recorded time, excluding pauses.</param>
/// <param name="VideoTracks">2.0: the session's video tracks (<c>[]</c> without video), as <c>recording.videoStatus</c> reports them.</param>
public sealed record RecordingStatePayload(
    string SessionId,
    string RecordingId,
    string State,
    DateTimeOffset StartedAt,
    long ElapsedMs,
    IReadOnlyList<Track> Tracks,
    DateTimeOffset? LastCheckpointAt,
    int HighlightsCount,
    IReadOnlyList<VideoTrackStatus>? VideoTracks = null)
{
    /// <summary>Never null on the wire: <c>[]</c> for a session without video.</summary>
    public IReadOnlyList<VideoTrackStatus> VideoTracks { get; init; } = VideoTracks ?? [];
}
