namespace Memento.Audio.Recording;

/// <summary>Why a track ended.</summary>
public enum TrackEndReason
{
    /// <summary>The session stopped (normal end, aligned with every other track).</summary>
    SessionStopped,

    /// <summary>The device, session or app went away (<see cref="TrackResult.EndedEarlyAt"/> says when).</summary>
    SourceLost,

    /// <summary>The user turned the source off mid-session.</summary>
    Disabled,

    /// <summary>Writing failed (disk full or I/O error); everything before it is kept.</summary>
    WriteFailed,
}
