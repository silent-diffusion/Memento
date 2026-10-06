namespace Memento.Core.Projects;

/// <summary>The playback/export mixdown.</summary>
/// <param name="File">Relative path, e.g. <c>mix.flac</c>.</param>
public sealed record ProjectMix(string File, string Codec, int SampleRate, int Channels, long DurationMs, string Sha256);
