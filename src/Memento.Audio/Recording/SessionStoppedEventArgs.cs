namespace Memento.Audio.Recording;

/// <summary>The session stopped, by request or by the host (disk full, write failure, every source lost).</summary>
public sealed class SessionStoppedEventArgs(AudioSessionResult result) : EventArgs
{
    public AudioSessionResult Result { get; } = result;
}
