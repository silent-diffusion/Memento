namespace Memento.Core.Projects;

/// <summary>The playback/export mixdown.</summary>
/// <param name="File">Relative path, e.g. <c>mix.flac</c>.</param>
public sealed record ProjectMix(string File, string Codec, int SampleRate, int Channels, long DurationMs, string Sha256)
{
    /// <summary>
    /// "Even out source levels" (after 2.0.0-beta.1): the gains each track got when this mix was built; <c>null</c> when
    /// the tracks were summed as recorded. An optional field within schema 4; earlier builds ignore it.
    /// </summary>
    public ProjectMixLevels? EvenLevels { get; init; }
}
