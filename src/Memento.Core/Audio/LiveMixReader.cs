using Memento.Core.Recording;

namespace Memento.Core.Audio;

/// <summary>
/// Builds the live transcript's audio (2.0) from what the track writers have already put on disk: each window is read
/// from the growing capture WAVs (opened to read while they are written, every RIFF part followed), each track mixed to
/// mono, resampled to 16 kHz with a box filter and placed on the recording timeline by its start offset, then the tracks
/// are summed and scaled down only if the sum would clip. It never touches the capture or writer threads: writers flush
/// once a second and at every checkpoint, and this reads behind them.
/// </summary>
public static class LiveMixReader
{
    public const int SampleRate = 16_000;

    private const int ChunkFrames = 4096;

    /// <summary>How far (on the recording timeline) <paramref name="track"/>'s samples on disk reach, in milliseconds.</summary>
    /// <exception cref="IOException">The file could not be read.</exception>
    public static long CoveredUntilMs(LiveTrackSource track)
    {
        ArgumentNullException.ThrowIfNull(track);
        long frames = 0;
        var rate = 0;
        foreach (var part in CaptureParts.Find(track.ProjectFolder, track.File))
        {
            using var stream = Open(track.ProjectFolder, part);
            var info = WavInfo.Read(stream);
            rate = info.Format.SampleRate;
            frames += Math.Max(0, stream.Length - info.DataOffset) / info.Format.BlockAlign;
        }

        return rate == 0 ? track.StartOffsetMs : track.StartOffsetMs + (frames * 1000 / rate);
    }

    /// <summary>The mix of <paramref name="tracks"/> from <paramref name="startMs"/> to <paramref name="endMs"/> at 16 kHz mono.</summary>
    /// <exception cref="IOException">A file could not be read.</exception>
    /// <exception cref="InvalidDataException">A file is not a WAV Memento can read.</exception>
    public static LiveMix Read(IReadOnlyList<LiveTrackSource> tracks, long startMs, long endMs)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        ArgumentOutOfRangeException.ThrowIfNegative(startMs);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(endMs, startMs);
        var length = (int)((endMs - startMs) * SampleRate / 1000);
        var mix = new float[length];
        var heard = 0;
        foreach (var track in tracks)
        {
            var from = Math.Max(startMs, track.StartOffsetMs);
            var to = track.EndedAtMs is { } ended ? Math.Min(endMs, ended) : endMs;
            if (to <= from)
            {
                continue;
            }

            var mono = ReadMono(track, from - track.StartOffsetMs, to - track.StartOffsetMs, out var rate);
            if (mono.Length == 0)
            {
                continue;
            }

            var resampled = Resample(mono, rate, SampleRate);
            var at = (int)((from - startMs) * SampleRate / 1000);
            var count = Math.Min(resampled.Length, length - at);
            for (var i = 0; i < count; i++)
            {
                mix[at + i] += resampled[i];
            }

            heard++;
        }

        var peak = 0f;
        double sum = 0;
        foreach (var sample in mix)
        {
            peak = Math.Max(peak, Math.Abs(sample));
            sum += sample * sample;
        }

        if (peak > 1f)
        {
            for (var i = 0; i < mix.Length; i++)
            {
                mix[i] /= peak;
            }

            sum /= peak * peak;
        }

        return new LiveMix(mix, mix.Length == 0 ? 0 : Math.Sqrt(sum / mix.Length), heard);
    }

    /// <summary>Averages every output sample over the input samples it covers (a box low-pass), then picks it.</summary>
    internal static float[] Resample(float[] input, int fromRate, int toRate)
    {
        if (fromRate == toRate)
        {
            return input;
        }

        var outputLength = (int)((long)input.Length * toRate / fromRate);
        var output = new float[outputLength];
        var step = (double)fromRate / toRate;
        var prefix = new double[input.Length + 1];
        for (var i = 0; i < input.Length; i++)
        {
            prefix[i + 1] = prefix[i] + input[i];
        }

        for (var i = 0; i < outputLength; i++)
        {
            var start = (int)Math.Floor(i * step);
            var end = Math.Min(input.Length, Math.Max(start + 1, (int)Math.Floor((i + 1) * step)));
            output[i] = (float)((prefix[end] - prefix[start]) / (end - start));
        }

        return output;
    }

    private static float[] ReadMono(LiveTrackSource track, long fromMs, long toMs, out int rate)
    {
        rate = 0;
        float[]? mono = null;
        var filled = 0;
        long skip = -1;
        foreach (var part in CaptureParts.Find(track.ProjectFolder, track.File))
        {
            using var stream = Open(track.ProjectFolder, part);
            var info = WavInfo.Read(stream);
            var format = info.Format;
            if (mono is null)
            {
                rate = format.SampleRate;
                skip = fromMs * rate / 1000;
                mono = new float[(int)((toMs - fromMs) * rate / 1000)];
            }

            var available = Math.Max(0, stream.Length - info.DataOffset) / format.BlockAlign;
            if (skip >= available)
            {
                skip -= available;
                continue;
            }

            stream.Seek(info.DataOffset + (skip * format.BlockAlign), SeekOrigin.Begin);
            var left = Math.Min(available - skip, mono.Length - filled);
            skip = 0;
            var bytes = new byte[ChunkFrames * format.BlockAlign];
            var samples = new float[ChunkFrames * format.Channels];
            while (left > 0)
            {
                var want = (int)Math.Min(ChunkFrames, left);
                var got = ReadFrames(stream, bytes, want * format.BlockAlign) / format.BlockAlign;
                if (got == 0)
                {
                    break;
                }

                WavReader.DecodeSamples(format, bytes.AsSpan(0, got * format.BlockAlign), samples.AsSpan(0, got * format.Channels));
                for (var f = 0; f < got; f++)
                {
                    float value = 0;
                    for (var c = 0; c < format.Channels; c++)
                    {
                        value += samples[(f * format.Channels) + c];
                    }

                    mono[filled++] = value / format.Channels;
                }

                left -= got;
            }

            if (filled == mono.Length)
            {
                break;
            }
        }

        return mono is null ? [] : mono[..filled];
    }

    private static int ReadFrames(Stream stream, byte[] buffer, int wanted)
    {
        var total = 0;
        while (total < wanted)
        {
            var n = stream.Read(buffer, total, wanted - total);
            if (n == 0)
            {
                break;
            }

            total += n;
        }

        return total;
    }

    /// <summary>Opened to read while the writer has it open, and while a later step may move or delete it.</summary>
    private static FileStream Open(string folder, string relative) =>
        new(Path.Combine(folder, relative.Replace('/', Path.DirectorySeparatorChar)), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16);
}
