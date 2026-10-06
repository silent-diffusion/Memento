using System.Buffers.Binary;

namespace Memento.Core.Audio;

/// <summary>
/// Mixes tracks into one 16-bit PCM WAV for playback and export. Every input is resampled to the highest input
/// rate and mapped to a common channel count (stereo if any input has two or more channels, unless downmixing),
/// placed at its start offset, and summed. Sums are protected from clipping by a soft limiter above
/// <see cref="LimiterThreshold"/>, so a loud passage bends instead of wrapping or square-clipping.
/// Streams in blocks: memory does not grow with recording length.
/// </summary>
public static class PcmMixer
{
    /// <summary>Samples below this magnitude pass unchanged; above it they are compressed towards ±1.</summary>
    public const float LimiterThreshold = 0.9f;

    private const int BlockFrames = 4096;

    /// <summary>Writes the mix to <paramref name="outputPath"/> and builds its waveform peaks in the same pass.</summary>
    /// <param name="downmixMono">Mix to one channel whatever the inputs.</param>
    public static MixResult Mix(
        IReadOnlyList<MixInput> inputs,
        string outputPath,
        bool downmixMono,
        int peakWindowMs,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        var readers = new List<WavReader>(inputs.Count);
        var sources = new List<(ResamplingReader Reader, long StartFrame, long EndFrame)>(inputs.Count);
        try
        {
            foreach (var input in inputs)
            {
                readers.Add(new WavReader(input.WavPath));
            }

            var rate = readers.Count == 0 ? 48_000 : readers.Max(r => r.Format.SampleRate);
            var channels = downmixMono || readers.Count == 0 || readers.All(r => r.Format.Channels == 1) ? 1 : 2;
            var format = PcmFormat.Pcm16(rate, channels);
            for (var i = 0; i < readers.Count; i++)
            {
                var resampler = new ResamplingReader(readers[i], rate, channels);
                var start = Math.Max(0, inputs[i].StartOffsetMs) * rate / 1000;
                sources.Add((resampler, start, start + resampler.OutputFrames));
            }

            readers.Clear(); // now owned by the resamplers
            var totalFrames = sources.Count == 0 ? 0 : sources.Max(s => s.EndFrame);
            var peaks = new PeakBuilder(rate, channels, peakWindowMs);
            var clipped = WriteMix(sources, format, totalFrames, outputPath, peaks, cancellationToken);
            peaks.Complete();
            return new MixResult(format, totalFrames, format.FramesToMilliseconds(totalFrames), clipped, peaks);
        }
        finally
        {
            foreach (var reader in readers)
            {
                reader.Dispose();
            }

            foreach (var source in sources)
            {
                source.Reader.Dispose();
            }
        }
    }

    /// <summary>The limiter curve: identity below the threshold, a tanh knee above, never reaching ±1.</summary>
    public static float Limit(float sample)
    {
        var magnitude = MathF.Abs(sample);
        if (magnitude <= LimiterThreshold)
        {
            return sample;
        }

        const float headroom = 1f - LimiterThreshold;
        var bent = LimiterThreshold + (headroom * MathF.Tanh((magnitude - LimiterThreshold) / headroom));
        return MathF.CopySign(bent, sample);
    }

    private static long WriteMix(
        List<(ResamplingReader Reader, long StartFrame, long EndFrame)> sources,
        PcmFormat format,
        long totalFrames,
        string outputPath,
        PeakBuilder peaks,
        CancellationToken cancellationToken)
    {
        var channels = format.Channels;
        var mix = new float[BlockFrames * channels];
        var scratch = new float[BlockFrames * channels];
        var pcm = new byte[BlockFrames * format.BlockAlign];
        long clipped = 0;

        if (File.Exists(outputPath))
        {
            File.Delete(outputPath);
        }

        using var writer = new StreamingWavWriter(outputPath, format);
        for (long position = 0; position < totalFrames; position += BlockFrames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var frames = (int)Math.Min(BlockFrames, totalFrames - position);
            Array.Clear(mix, 0, frames * channels);
            foreach (var (reader, start, end) in sources)
            {
                if (end <= position || start >= position + frames)
                {
                    continue;
                }

                var offset = (int)Math.Max(0, start - position);
                var want = frames - offset;
                var got = reader.Read(scratch, want);
                var target = offset * channels;
                for (var i = 0; i < got * channels; i++)
                {
                    mix[target + i] += scratch[i];
                }
            }

            var samples = frames * channels;
            for (var i = 0; i < samples; i++)
            {
                var limited = Limit(mix[i]);
                if (limited != mix[i])
                {
                    clipped++;
                }

                mix[i] = limited;
                BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i * 2), (short)MathF.Round(limited * short.MaxValue));
            }

            peaks.Add(mix.AsSpan(0, samples));
            writer.Write(pcm.AsSpan(0, samples * 2));
        }

        return clipped;
    }
}
