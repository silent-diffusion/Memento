namespace Memento.Audio.Recording;

/// <summary>Everything a stopped session produced.</summary>
/// <param name="StartedAt">Wall-clock time of the session start (local offset).</param>
/// <param name="StoppedAt">Wall-clock time of the stop.</param>
/// <param name="Duration">Timeline duration (pauses excluded).</param>
/// <param name="Gaps">Pauses, in timeline order.</param>
/// <param name="Tracks">Every track, including ones that ended early and ones disabled mid-session.</param>
/// <param name="StopReason">Why it stopped.</param>
/// <param name="StopMessage">Specific message when the host stopped it (disk full, all sources lost).</param>
public sealed record AudioSessionResult(
    DateTimeOffset StartedAt,
    DateTimeOffset StoppedAt,
    TimeSpan Duration,
    IReadOnlyList<SessionGap> Gaps,
    IReadOnlyList<TrackResult> Tracks,
    SessionStopReason StopReason,
    string? StopMessage);
