namespace Memento.Audio.Recording;

/// <summary>Why a session stopped (maps to BRIDGE.md <c>recording.stoppedByHost.reason</c> when not requested).</summary>
public enum SessionStopReason
{
    /// <summary><see cref="AudioRecordingSession.StopAsync"/> was called.</summary>
    Requested,

    /// <summary>A write failed for lack of space.</summary>
    DiskFull,

    /// <summary>A write failed for another reason.</summary>
    WriteFailed,

    /// <summary>The last remaining source was lost.</summary>
    AllSourcesLost,
}
