namespace Memento.Core.Recording;

/// <summary>The session stopped by itself; the result is final and every track is closed.</summary>
public sealed class HostStoppedEventArgs(HostStopReason reason, long atMs, string detail, RecordingSessionResult result) : EventArgs
{
    public HostStopReason Reason { get; } = reason;

    /// <summary>Recorded time at the stop.</summary>
    public long AtMs { get; } = atMs;

    /// <summary>Diagnostic detail for the log (never shown as is).</summary>
    public string Detail { get; } = detail;

    public RecordingSessionResult Result { get; } = result;
}
