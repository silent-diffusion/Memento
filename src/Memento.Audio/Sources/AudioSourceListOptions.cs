namespace Memento.Audio.Sources;

/// <summary>Options for <see cref="AudioSourceEnumerator.List"/>.</summary>
public sealed record AudioSourceListOptions
{
    public static AudioSourceListOptions Default { get; } = new();

    /// <summary>Extract a 32×32 PNG icon for each application (a few ms per app).</summary>
    public bool IncludeIcons { get; init; }

    /// <summary>Hide Memento's own audio session (recording our own playback is never wanted).</summary>
    public bool ExcludeOwnProcess { get; init; } = true;
}
