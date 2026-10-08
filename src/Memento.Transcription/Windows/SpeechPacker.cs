namespace Memento.Transcription.Windows;

/// <summary>
/// Prepares a window for the engine (ENGINE-NOTES.md §K). Every sound region is kept with <see cref="PadSeconds"/> of
/// the audio around it and longer silences are cut in the middle, so a pause of any length reaches Whisper as 0.8 s of
/// the room's own quiet; the result is then cut into chunks of at most <see cref="ChunkSeconds"/>, at a shortened pause
/// when there is one and otherwise at the quietest moment, so each chunk is one 30-second pass of the engine.
/// Whisper places a line's start anywhere in the pause before it (seconds early after a long one), drops the words its
/// own 30-second cut falls in, and after half a minute of silence with a prompt can repeat one sentence for minutes;
/// with short pauses and chunks cut between words it does none of these. Times come back through
/// <see cref="PackedAudio"/>.
/// </summary>
public static class SpeechPacker
{
    /// <summary>Audio kept before and after each sound region.</summary>
    public const double PadSeconds = 0.4;

    /// <summary>Longest chunk handed to the engine at once (its own window is 30 s).</summary>
    public const double ChunkSeconds = 28;

    /// <summary>Length of the stretches compared when a chunk has to be cut inside sound.</summary>
    private const double QuietProbeSeconds = 0.05;

    /// <param name="samples">The window's audio (mono).</param>
    /// <param name="sampleRate">Its rate.</param>
    /// <param name="windowStart">Track time of the first sample, in seconds.</param>
    /// <param name="regions">The track's sound regions (<see cref="Audio.SpeechEnergy.SoundRegions"/>), in track seconds, sorted.</param>
    /// <returns>The chunks in order; one chunk with the whole window when nothing in it is loud enough to keep.</returns>
    public static IReadOnlyList<PackedAudio> Pack(
        float[] samples,
        int sampleRate,
        double windowStart,
        IReadOnlyList<(double Start, double End)> regions,
        double pad = PadSeconds,
        double chunkSeconds = ChunkSeconds)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(regions);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkSeconds, 1.0);
        var length = samples.Length / (double)sampleRate;

        // Sound regions in window samples with their pads, clipped to the window; overlapping ones joined.
        var spans = new List<(int First, int End)>();
        foreach (var (start, end) in regions)
        {
            var first = (int)Math.Round(Math.Max(0, start - windowStart - pad) * sampleRate);
            var last = (int)Math.Round(Math.Min(length, end - windowStart + pad) * sampleRate);
            if (last <= first)
            {
                continue;
            }

            if (spans.Count > 0 && first <= spans[^1].End)
            {
                spans[^1] = (spans[^1].First, Math.Max(spans[^1].End, last));
            }
            else
            {
                spans.Add((first, last));
            }
        }

        if (spans.Count == 0)
        {
            // Nothing heard in the window: it goes to the engine as it is.
            return [new PackedAudio(samples, [new SpeechPiece(0, length, 0)])];
        }

        // Chunks of whole spans while they fit; a span longer than a chunk is cut at its quietest moment.
        var limit = (int)(chunkSeconds * sampleRate);
        var chunks = new List<List<(int First, int End)>>();
        var current = new List<(int First, int End)>();
        var used = 0;
        foreach (var span in spans)
        {
            var (first, end) = span;
            while (end > first)
            {
                var size = end - first;
                if (used + size <= limit)
                {
                    current.Add((first, end));
                    used += size;
                    break;
                }

                if (used > 0)
                {
                    chunks.Add(current);
                    current = [];
                    used = 0;
                    continue;
                }

                var cut = QuietestCut(samples, sampleRate, first + (limit / 2), first + limit);
                current.Add((first, cut));
                chunks.Add(current);
                current = [];
                first = cut;
            }
        }

        if (current.Count > 0)
        {
            chunks.Add(current);
        }

        return chunks.Select(c => Build(samples, sampleRate, c)).ToList();
    }

    private static PackedAudio Build(float[] samples, int sampleRate, List<(int First, int End)> spans)
    {
        var total = spans.Sum(s => s.End - s.First);
        var packed = new float[total];
        var pieces = new List<SpeechPiece>(spans.Count);
        var at = 0;
        foreach (var (first, end) in spans)
        {
            Array.Copy(samples, first, packed, at, end - first);
            pieces.Add(new SpeechPiece(first / (double)sampleRate, end / (double)sampleRate, at / (double)sampleRate));
            at += end - first;
        }

        return new PackedAudio(packed, pieces);
    }

    /// <summary>The start of the quietest short stretch between <paramref name="from"/> and <paramref name="to"/> (samples).</summary>
    private static int QuietestCut(float[] samples, int sampleRate, int from, int to)
    {
        var probe = Math.Max(1, (int)(QuietProbeSeconds * sampleRate));
        var best = to;
        var bestEnergy = double.MaxValue;
        for (var at = from; at + probe <= to; at += probe)
        {
            double energy = 0;
            for (var i = at; i < at + probe; i++)
            {
                energy += samples[i] * (double)samples[i];
            }

            if (energy < bestEnergy)
            {
                bestEnergy = energy;
                best = at + (probe / 2);
            }
        }

        return best;
    }
}
