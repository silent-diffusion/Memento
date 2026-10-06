using Memento.Audio.Codecs;
using Memento.Audio.Writing;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Memento.Audio.Mixing;

/// <summary>
/// Sums N tracks (any channel count, any rate) into one stereo int24 WAV on the recording timeline: each track
/// starts at its offset and stops at its ended-early time, so the mix lines up with every track. A soft limiter
/// keeps the sum below full scale. The mix is written as <c>&lt;stem&gt;.partial*.wav</c> and renamed to
/// <c>&lt;stem&gt;.wav</c> (and <c>.partN.wav</c>) only when complete; an existing mix is never overwritten.
/// </summary>
public static class TrackMixer
{
    private const int BlockFrames = 4_800;

    public static Task<MixResult> MixAsync(IReadOnlyList<MixInput> inputs, string directory, string stem, CancellationToken cancellationToken) =>
        MixAsync(inputs, directory, stem, MixOptions.Default, null, cancellationToken);

    public static Task<MixResult> MixAsync(IReadOnlyList<MixInput> inputs, string directory, string stem, MixOptions? options, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(stem);
        if (inputs.Count == 0)
        {
            throw new ArgumentException("A mix needs at least one track.", nameof(inputs));
        }

        if (WavTrackSet.FindParts(directory, stem).Count > 0)
        {
            throw new IOException($"{WavTrackSet.PartFileName(stem, 1)} already exists; the mix is never overwritten. Remove it first to mix again.");
        }

        return Task.Run(() => Mix(inputs, directory, stem, options ?? MixOptions.Default, progress, cancellationToken), cancellationToken);
    }

    private static MixResult Mix(IReadOnlyList<MixInput> inputs, string directory, string stem, MixOptions options, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var opened = new List<Source>();
        var partialStem = stem + ".partial";
        RollingWavWriter? writer = null;
        try
        {
            var decoded = inputs.Select(i => (Input: i, Audio: i.Open())).ToList();
            opened.AddRange(decoded.Select(d => new Source(d.Input, d.Audio)));
            var rate = options.SampleRate ?? decoded.Max(d => d.Audio.SampleRate);
            foreach (var s in opened)
            {
                s.Prepare(rate);
            }

            var format = AudioFormat.Pcm24(rate, 2);
            var totalFrames = opened.Max(s => s.EstimatedEndFrame);
            writer = new RollingWavWriter(directory, partialStem, format, options.RolloverBytes);
            var mix = new float[BlockFrames * 2];
            var scratch = new float[BlockFrames * 2];
            var bytes = new byte[BlockFrames * 2 * 3];
            long position = 0;
            float peak = 0;
            long limited = 0;
            while (opened.Any(s => !s.Done))
            {
                cancellationToken.ThrowIfCancellationRequested();
                Array.Clear(mix);
                var blockEnd = position + BlockFrames;
                var produced = 0L;
                foreach (var s in opened)
                {
                    produced = Math.Max(produced, s.AddInto(mix, scratch, position, blockEnd) - position);
                }

                if (produced <= 0)
                {
                    // Every remaining source is still before its start: emit the block as silence.
                    produced = opened.Any(s => !s.Done) ? BlockFrames : 0;
                }

                var samples = (int)produced * 2;
                for (var i = 0; i < samples; i++)
                {
                    var v = mix[i];
                    var a = MathF.Abs(v);
                    if (a > peak)
                    {
                        peak = a;
                    }

                    if (a > options.LimiterThreshold)
                    {
                        limited++;
                        mix[i] = SoftLimiter.Apply(v, options.LimiterThreshold);
                    }
                }

                PcmConverter.FloatToInt24(mix.AsSpan(0, samples), bytes);
                writer.Write(bytes.AsSpan(0, samples * 3));
                position += produced;
                progress?.Report(totalFrames > 0 ? Math.Min(1, position / (double)totalFrames) : 0);
            }

            writer.Dispose();
            var partial = writer.Parts.ToList();
            writer = null;
            var finalParts = new List<string>();
            for (var i = 0; i < partial.Count; i++)
            {
                var target = Path.Combine(directory, WavTrackSet.PartFileName(stem, i + 1));
                File.Move(partial[i], target);
                finalParts.Add(target);
            }

            progress?.Report(1);
            return new MixResult(finalParts, format, position, format.DurationOf(position), peak, limited);
        }
        catch
        {
            if (writer is not null)
            {
                writer.Dispose();
                foreach (var part in writer.Parts)
                {
                    File.Delete(part);
                }
            }

            throw;
        }
        finally
        {
            foreach (var s in opened)
            {
                s.Dispose();
            }
        }
    }

    /// <summary>One input converted to stereo at the mix rate, with its timeline window in frames.</summary>
    private sealed class Source(MixInput input, DecodedAudio audio) : IDisposable
    {
        private ISampleProvider _samples = audio;
        private long _start;
        private long _end = long.MaxValue;

        public bool Done { get; private set; }

        public long EstimatedEndFrame { get; private set; }

        public void Prepare(int rate)
        {
            ISampleProvider p = audio.Channels == 2 ? audio : new ChannelMapSampleProvider(audio, 2);
            if (p.WaveFormat.SampleRate != rate)
            {
                p = new WdlResamplingSampleProvider(p, rate);
            }

            _samples = p;
            _start = QpcClock.TicksToFramesRounded(input.StartOffset.Ticks, rate);
            if (input.EndedAt is { } end)
            {
                _end = QpcClock.TicksToFramesRounded(end.Ticks, rate);
            }

            var length = audio.Duration is { } d ? QpcClock.TicksToFramesRounded(d.Ticks, rate) : 0;
            EstimatedEndFrame = Math.Min(_end, _start + length);
            if (_end <= _start)
            {
                Done = true;
            }
        }

        /// <summary>Adds this source's frames for [blockStart, blockEnd) into <paramref name="mix"/>; returns the timeline frame it reached.</summary>
        public long AddInto(float[] mix, float[] scratch, long blockStart, long blockEnd)
        {
            if (Done)
            {
                return blockStart;
            }

            var from = Math.Max(blockStart, _start);
            var to = Math.Min(blockEnd, _end);
            if (from >= to)
            {
                if (blockStart >= _end)
                {
                    Done = true;
                }

                // Not started yet (or already past its end): contributes nothing to this block.
                return blockStart;
            }

            var want = (int)(to - from) * 2;
            var got = 0;
            while (got < want)
            {
                var n = _samples.Read(scratch, got, want - got);
                if (n == 0)
                {
                    Done = true;
                    break;
                }

                got += n;
            }

            got -= got % 2;
            var offset = (int)(from - blockStart) * 2;
            var gain = input.Gain;
            for (var i = 0; i < got; i++)
            {
                mix[offset + i] += scratch[i] * gain;
            }

            if (from + (got / 2) >= _end)
            {
                Done = true;
            }

            return from + (got / 2);
        }

        public void Dispose() => audio.Dispose();
    }
}
