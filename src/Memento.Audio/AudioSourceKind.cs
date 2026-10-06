namespace Memento.Audio;

/// <summary>What a recording source captures (BRIDGE.md <c>AudioSource.kind</c>).</summary>
public enum AudioSourceKind
{
    /// <summary>An active capture endpoint, recorded directly.</summary>
    Microphone,

    /// <summary>Everything a render endpoint plays, recorded through endpoint loopback.</summary>
    System,

    /// <summary>One process tree's audio, recorded through process loopback.</summary>
    Application,
}
