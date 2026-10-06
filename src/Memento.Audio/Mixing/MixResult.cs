namespace Memento.Audio.Mixing;

/// <summary>A finished mixdown.</summary>
/// <param name="Parts">The WAV parts written (<c>mix.wav</c>, <c>mix.part2.wav</c>, …), ready for FLAC encoding.</param>
/// <param name="Format">Stereo int24 at the mix rate.</param>
/// <param name="Frames">Frames written.</param>
/// <param name="Duration">Mix duration (the latest track end on the timeline).</param>
/// <param name="PeakBeforeLimiter">Largest summed magnitude before the limiter (above 1 means it prevented clipping).</param>
/// <param name="LimitedSamples">Samples the limiter bent.</param>
public sealed record MixResult(IReadOnlyList<string> Parts, AudioFormat Format, long Frames, TimeSpan Duration, float PeakBeforeLimiter, long LimitedSamples);
