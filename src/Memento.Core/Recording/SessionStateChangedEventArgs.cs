namespace Memento.Core.Recording;

/// <summary>The session paused, resumed or stopped.</summary>
public sealed class SessionStateChangedEventArgs(RecordingSessionState state, long elapsedMs) : EventArgs
{
    public RecordingSessionState State { get; } = state;

    public long ElapsedMs { get; } = elapsedMs;
}
