namespace Memento.Audio.Sources;

/// <summary>The source list may be different now; re-run <see cref="AudioSourceEnumerator.List"/>.</summary>
public sealed class AudioSourcesChangedEventArgs(AudioSourceChanges changes) : EventArgs
{
    public AudioSourceChanges Changes { get; } = changes;
}
