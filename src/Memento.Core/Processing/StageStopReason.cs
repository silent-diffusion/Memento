namespace Memento.Core.Processing;

/// <summary>Why a running stage's token was cancelled; the stage reads it to decide what to record.</summary>
public enum StageStopReason
{
    /// <summary>Not stopped.</summary>
    None,

    /// <summary>The processing gate closed (PC busy, recording, low disk, paused by the user): it resumes later.</summary>
    Paused,

    /// <summary><c>processing.cancel</c>: keep what was made and record the stage as failed (cancelled).</summary>
    Cancelled,

    /// <summary>Memento is closing: the stage stays queued and resumes at the next launch.</summary>
    Shutdown,

    /// <summary>The recording is being deleted.</summary>
    Deleted,

    /// <summary>The stage was queued again with other options (a new transcription pass): it starts over.</summary>
    Requeued,
}
