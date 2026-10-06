namespace Memento.Core.Audio;

/// <summary>What <see cref="PcmMixer.Mix"/> wrote.</summary>
/// <param name="ClippedSamples">Samples the limiter had to bend because the sum went past its threshold.</param>
/// <param name="Peaks">Waveform peaks of the mix, complete.</param>
public sealed record MixResult(PcmFormat Format, long Frames, long DurationMs, long ClippedSamples, PeakBuilder Peaks);
