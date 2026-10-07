namespace Memento.Core.Import;

/// <summary>What <see cref="IMediaDecoder.ProbeAsync"/> found.</summary>
/// <param name="DurationMs">From the container; 0 when it does not say.</param>
/// <param name="HasVideo">The file also has a video stream, which import leaves out.</param>
public sealed record MediaProbe(int SampleRate, int Channels, long DurationMs, bool HasVideo);
