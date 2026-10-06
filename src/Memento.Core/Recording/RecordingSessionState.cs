namespace Memento.Core.Recording;

/// <summary>Capture state of a session. Finalizing and ready are the coordinator's states, not the engine's.</summary>
public enum RecordingSessionState
{
    Recording,
    Paused,
    Stopped,
}
