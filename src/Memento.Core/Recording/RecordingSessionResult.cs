namespace Memento.Core.Recording;

/// <summary>A stopped session: every track closed with a valid header.</summary>
/// <param name="StoppedBy">Set when the session stopped by itself.</param>
public sealed record RecordingSessionResult(
    long ElapsedMs,
    IReadOnlyList<SessionTrack> Tracks,
    IReadOnlyList<SessionPause> Pauses,
    DateTimeOffset StoppedAt,
    HostStopReason? StoppedBy);
