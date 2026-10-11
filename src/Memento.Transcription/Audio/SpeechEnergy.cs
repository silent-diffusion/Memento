using Memento.Core.Audio;

namespace Memento.Transcription.Audio;

/// <summary>
/// A simple energy voice-activity measure over 16 kHz mono audio, fed block by block: the RMS of every 100 ms frame.
/// Every bar is relative to the track itself (ENGINE-NOTES.md §Q): a frame is speech when its RMS is above both the
/// track's noise floor (its 10th-percentile frame) times <see cref="SpeechNoiseFactor"/> and a floor that follows the
/// track's own speech level (<see cref="SpeechLevel"/>: −40 dBFS for speech at −20 dBFS or louder, 20 dB under it for
/// quieter speech, never below −70 dBFS), so a quiet microphone is judged by its own loudness, not by a fixed bar a
/// loud track clears. Speech frames closer than 0.5 s are joined and regions shorter than 0.3 s dropped. A track with
/// less than one second of speech is silent and is not transcribed. Each region's edges are then refined to the first
/// and last 10 ms above the same threshold, so a region starts within 10 ms of where its sound starts.
/// <see cref="SoundRegions"/> is the same with a lower bar (−50 dBFS, or 30 dB under the speech level, never below
/// −75 dBFS; and <see cref="SoundNoiseFactor"/> times the noise floor), for shortening silences and aligning line
/// times (ENGINE-NOTES.md §K).
/// </summary>
public sealed class SpeechEnergy(int sampleRate = 16_000)
{
    public const double FrameSeconds = 0.1;
    public const double FineSeconds = 0.01;

    /// <summary>The speech bar for a track whose speech is at −20 dBFS or louder: −40 dBFS.</summary>
    public const double AbsoluteFloor = 0.01;

    /// <summary>The sound bar for a track whose speech is at −20 dBFS or louder: −50 dBFS.</summary>
    public const double SoundFloor = 0.00316;

    /// <summary>The lowest the speech bar goes for quiet speech: −70 dBFS.</summary>
    public const double MinSpeechFloor = 0.000316;

    /// <summary>The lowest the sound bar goes for quiet speech: −75 dBFS.</summary>
    public const double MinSoundFloor = 0.000178;

    /// <summary>Under −20 dBFS of speech, the speech bar sits this far under the track's speech level.</summary>
    public const double SpeechBelowLevelDb = 20;

    /// <summary>Under −20 dBFS of speech, the sound bar sits this far under the track's speech level.</summary>
    public const double SoundBelowLevelDb = 30;

    /// <summary>Speech is at least this many times the noise floor.</summary>
    public const double SpeechNoiseFactor = 2;

    /// <summary>Sound is at least this many times the noise floor.</summary>
    public const double SoundNoiseFactor = 1.6;

    public const double MinSpeechSeconds = 1.0;
    private const double JoinSeconds = 0.5;
    private const double MinRegionSeconds = 0.3;
    private const int FinePerFrame = 10;

    private readonly int _frameLength = (int)(sampleRate * FrameSeconds);
    private readonly int _fineLength = (int)(sampleRate * FineSeconds);
    private readonly List<float> _frames = [];
    private readonly List<float> _fine = [];
    private double _sum;
    private int _count;
    private double _fineSum;
    private int _fineCount;
    private double _totalSquares;
    private long _totalSamples;

    /// <summary>Seconds of audio seen so far.</summary>
    public double DurationSeconds => _totalSamples / (double)sampleRate;

    /// <summary>RMS of the whole track.</summary>
    public double Rms => _totalSamples == 0 ? 0 : Math.Sqrt(_totalSquares / _totalSamples);

    /// <summary>The track's speech level and noise floor (<see cref="SpeechLevel"/>) over the frames seen so far.</summary>
    public SpeechLevel Level => SpeechLevel.FromFrames(Frames(), DurationSeconds);

    /// <summary>The bars <see cref="Regions"/> and <see cref="SoundRegions"/> use, linear, over the frames seen so far.</summary>
    public (double Speech, double Sound) Thresholds()
    {
        var frames = Frames();
        var (speechFloor, soundFloor) = Floors(SpeechLevel.FromFrames(frames, DurationSeconds));
        var noise = NoiseFloor(frames);
        return (Math.Max(speechFloor, noise * SpeechNoiseFactor), Math.Max(soundFloor, noise * SoundNoiseFactor));
    }

    /// <summary>The floors that follow the speech level: the fixed bars for speech at −20 dBFS or louder, lower for quieter speech.</summary>
    public static (double Speech, double Sound) Floors(SpeechLevel level)
    {
        if (!level.HasSpeech)
        {
            // No speech level to go by (steady noise, one constant tone, speech hardly above its noise): the noise
            // floor alone decides, down to the lowest bars.
            return (MinSpeechFloor, MinSoundFloor);
        }

        var speech = Math.Clamp(level.ActiveRms * SpeechLevel.FromDbfs(-SpeechBelowLevelDb), MinSpeechFloor, AbsoluteFloor);
        var sound = Math.Clamp(level.ActiveRms * SpeechLevel.FromDbfs(-SoundBelowLevelDb), MinSoundFloor, SoundFloor);
        return (speech, sound);
    }

    public void Add(ReadOnlySpan<float> samples)
    {
        foreach (var sample in samples)
        {
            var square = (double)sample * sample;
            _sum += square;
            _fineSum += square;
            _totalSquares += square;
            _count++;
            _fineCount++;
            _totalSamples++;
            if (_fineCount == _fineLength)
            {
                _fine.Add((float)Math.Sqrt(_fineSum / _fineCount));
                _fineSum = 0;
                _fineCount = 0;
            }

            if (_count == _frameLength)
            {
                _frames.Add((float)Math.Sqrt(_sum / _count));
                _sum = 0;
                _count = 0;
            }
        }
    }

    /// <summary>Speech regions in seconds from the start of the track.</summary>
    public IReadOnlyList<(double Start, double End)> Regions() => Detect(Thresholds().Speech);

    /// <summary>
    /// Everything audible, in seconds from the start of the track: the same measure with a lower bar, so quiet speech
    /// is in it too. The engine hears these (<see cref="Windows.SpeechPacker"/>) and line times are aligned to them
    /// (<see cref="Windows.SpeechAligner"/>).
    /// </summary>
    public IReadOnlyList<(double Start, double End)> SoundRegions() => Detect(Thresholds().Sound);

    private List<float> Frames() => _count > 0 ? [.. _frames, (float)Math.Sqrt(_sum / _count)] : _frames;

    /// <summary>The 10th-percentile frame. Every frame counts, digital silence included (loopback tracks are padded with zeros).</summary>
    private static double NoiseFloor(List<float> frames)
    {
        if (frames.Count == 0)
        {
            return 0;
        }

        var sorted = frames.Order().ToList();
        return sorted[(int)(sorted.Count * 0.1)];
    }

    private List<(double Start, double End)> Detect(double threshold)
    {
        var frames = Frames();
        if (frames.Count == 0)
        {
            return [];
        }

        var fine = _fineCount > 0 ? [.. _fine, (float)Math.Sqrt(_fineSum / _fineCount)] : _fine;

        // Runs of speech frames (first and last frame index), joined across short pauses.
        var runs = new List<(int First, int Last)>();
        for (var i = 0; i < frames.Count; i++)
        {
            if (frames[i] <= threshold)
            {
                continue;
            }

            if (runs.Count > 0 && (i - runs[^1].Last - 1) * FrameSeconds <= JoinSeconds)
            {
                runs[^1] = (runs[^1].First, i);
            }
            else
            {
                runs.Add((i, i));
            }
        }

        var regions = new List<(double Start, double End)>();
        foreach (var (first, last) in runs)
        {
            var start = first * FrameSeconds;
            var end = Math.Min((last + 1) * FrameSeconds, DurationSeconds);
            if (end - start < MinRegionSeconds)
            {
                continue;
            }

            // A frame above the threshold has at least one 10 ms part above it: start at the first, end after the last.
            var firstFine = -1;
            for (var j = first * FinePerFrame; j < Math.Min(fine.Count, (first + 1) * FinePerFrame); j++)
            {
                if (fine[j] > threshold)
                {
                    firstFine = j;
                    break;
                }
            }

            var lastFine = -1;
            for (var j = Math.Min(fine.Count, (last + 1) * FinePerFrame) - 1; j >= last * FinePerFrame; j--)
            {
                if (fine[j] > threshold)
                {
                    lastFine = j;
                    break;
                }
            }

            if (firstFine >= 0)
            {
                start = firstFine * FineSeconds;
            }

            if (lastFine >= 0)
            {
                end = Math.Min((lastFine + 1) * FineSeconds, DurationSeconds);
            }

            regions.Add((Math.Round(start, 2), Math.Round(end, 2)));
        }

        return regions;
    }

    /// <summary>Less than <see cref="MinSpeechSeconds"/> of speech in the whole track.</summary>
    public static bool IsSilent(IReadOnlyList<(double Start, double End)> regions) =>
        regions.Sum(r => r.End - r.Start) < MinSpeechSeconds;

    /// <summary>Whether any region overlaps <paramref name="start"/>–<paramref name="end"/> (a window worth transcribing).</summary>
    public static bool HasSpeech(IReadOnlyList<(double Start, double End)> regions, double start, double end) =>
        regions.Any(r => r.Start < end && r.End > start);
}
