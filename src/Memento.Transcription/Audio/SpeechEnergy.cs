namespace Memento.Transcription.Audio;

/// <summary>
/// A simple energy voice-activity measure over 16 kHz mono audio, fed block by block: the RMS of every 100 ms frame.
/// A frame is speech when its RMS is above both an absolute floor (-40 dBFS) and three times the track's noise floor
/// (its 10th-percentile frame); speech frames closer than 0.5 s are joined and regions shorter than 0.3 s dropped. A
/// track with less than one second of speech is silent and is not transcribed.
/// </summary>
public sealed class SpeechEnergy(int sampleRate = 16_000)
{
    public const double FrameSeconds = 0.1;
    public const double AbsoluteFloor = 0.01;
    public const double MinSpeechSeconds = 1.0;
    private const double JoinSeconds = 0.5;
    private const double MinRegionSeconds = 0.3;

    private readonly int _frameLength = (int)(sampleRate * FrameSeconds);
    private readonly List<float> _frames = [];
    private double _sum;
    private int _count;
    private double _totalSquares;
    private long _totalSamples;

    /// <summary>Seconds of audio seen so far.</summary>
    public double DurationSeconds => _totalSamples / (double)sampleRate;

    /// <summary>RMS of the whole track.</summary>
    public double Rms => _totalSamples == 0 ? 0 : Math.Sqrt(_totalSquares / _totalSamples);

    public void Add(ReadOnlySpan<float> samples)
    {
        foreach (var sample in samples)
        {
            var square = (double)sample * sample;
            _sum += square;
            _totalSquares += square;
            _count++;
            _totalSamples++;
            if (_count == _frameLength)
            {
                _frames.Add((float)Math.Sqrt(_sum / _count));
                _sum = 0;
                _count = 0;
            }
        }
    }

    /// <summary>Speech regions in seconds from the start of the track.</summary>
    public IReadOnlyList<(double Start, double End)> Regions()
    {
        var frames = _count > 0 ? [.. _frames, (float)Math.Sqrt(_sum / _count)] : _frames;
        if (frames.Count == 0)
        {
            return [];
        }

        var sorted = frames.Where(f => f > 0).OrderBy(f => f).ToList();
        var noise = sorted.Count == 0 ? 0 : sorted[(int)(sorted.Count * 0.1)];
        var threshold = Math.Max(AbsoluteFloor, noise * 3);
        var regions = new List<(double Start, double End)>();
        for (var i = 0; i < frames.Count; i++)
        {
            if (frames[i] <= threshold)
            {
                continue;
            }

            var start = i * FrameSeconds;
            var end = Math.Min((i + 1) * FrameSeconds, DurationSeconds);
            if (regions.Count > 0 && start - regions[^1].End <= JoinSeconds)
            {
                regions[^1] = (regions[^1].Start, end);
            }
            else
            {
                regions.Add((start, end));
            }
        }

        return regions.Where(r => r.End - r.Start >= MinRegionSeconds).Select(r => (Math.Round(r.Start, 2), Math.Round(r.End, 2))).ToList();
    }

    /// <summary>Less than <see cref="MinSpeechSeconds"/> of speech in the whole track.</summary>
    public static bool IsSilent(IReadOnlyList<(double Start, double End)> regions) =>
        regions.Sum(r => r.End - r.Start) < MinSpeechSeconds;

    /// <summary>Whether any region overlaps <paramref name="start"/>–<paramref name="end"/> (a window worth transcribing).</summary>
    public static bool HasSpeech(IReadOnlyList<(double Start, double End)> regions, double start, double end) =>
        regions.Any(r => r.Start < end && r.End > start);
}
