namespace Memento.Core.Audio;

/// <summary>
/// How loud the speech in some audio is, measured against the audio's own noise floor (ENGINE-NOTES.md §Q), in the
/// spirit of ITU-T P.56's active speech level: the RMS of every 100 ms frame; the noise floor is the 10th-percentile frame
/// (none when a tenth or more of the audio is digital silence); a frame is active when it is <see cref="ActivityMarginDb"/> above the noise
/// floor and above <see cref="AbsoluteFloor"/>; the active level is the RMS over the active frames only, so pauses do
/// not pull it down. Audio with less than <see cref="MinActiveSeconds"/> of active frames has no speech (a level that
/// would only lift noise). Measured with <see cref="SpeechLevelMeter"/>, block by block.
/// </summary>
/// <param name="ActiveRms">RMS of the active frames, linear (0 when there are none).</param>
/// <param name="NoiseRms">The noise floor, linear (0 for digital silence).</param>
/// <param name="ActiveSeconds">How much of the audio is active.</param>
/// <param name="Seconds">How much audio was measured.</param>
public readonly record struct SpeechLevel(double ActiveRms, double NoiseRms, double ActiveSeconds, double Seconds)
{
    /// <summary>Frame length.</summary>
    public const double FrameSeconds = 0.1;

    /// <summary>A frame is active this far above the noise floor (+6 dB).</summary>
    public const double ActivityMarginDb = 6;

    /// <summary>Nothing below −70 dBFS counts as speech, whatever the noise floor.</summary>
    public const double AbsoluteFloor = 0.000316;

    /// <summary>Frames below −100 dBFS are digital silence (padding of loopback tracks) and not noise.</summary>
    public const double DigitalSilence = 0.00001;

    /// <summary>At least this much active audio before the level counts as speech.</summary>
    public const double MinActiveSeconds = 0.5;

    public static SpeechLevel None { get; } = new(0, 0, 0, 0);

    /// <summary>There is enough active audio to call it speech.</summary>
    public bool HasSpeech => ActiveSeconds >= MinActiveSeconds && ActiveRms > 0;

    /// <summary>The active level in dBFS (−∞ when there is none).</summary>
    public double ActiveDbfs => ToDbfs(ActiveRms);

    /// <summary>The noise floor in dBFS (−∞ for digital silence).</summary>
    public double NoiseDbfs => ToDbfs(NoiseRms);

    /// <summary>Measures <paramref name="samples"/> (mono) at once.</summary>
    public static SpeechLevel Measure(ReadOnlySpan<float> samples, int sampleRate)
    {
        var meter = new SpeechLevelMeter(sampleRate);
        meter.Add(samples);
        return meter.Result();
    }

    /// <summary>The level of 100 ms frame RMS values (what <see cref="SpeechLevelMeter"/> collects).</summary>
    public static SpeechLevel FromFrames(IReadOnlyList<float> frames, double seconds)
    {
        ArgumentNullException.ThrowIfNull(frames);
        var sounding = frames.Where(f => f >= DigitalSilence).Order().ToList();
        if (sounding.Count == 0)
        {
            return new SpeechLevel(0, 0, 0, seconds);
        }

        // The 10th-percentile frame; where a tenth or more is digital silence (loopback tracks are padded with zeros
        // while nothing plays) there is no noise floor, and everything that sounds above the absolute floor counts.
        var silentFrames = frames.Count - sounding.Count;
        var tenth = (int)(frames.Count * 0.1);
        var noise = tenth < silentFrames ? 0 : sounding[tenth - silentFrames];
        var threshold = Math.Max(AbsoluteFloor, noise * Math.Pow(10, ActivityMarginDb / 20));
        double squares = 0;
        var active = 0;
        foreach (var frame in sounding)
        {
            if (frame > threshold)
            {
                squares += (double)frame * frame;
                active++;
            }
        }

        return new SpeechLevel(active == 0 ? 0 : Math.Sqrt(squares / active), noise, active * FrameSeconds, seconds);
    }

    public static double ToDbfs(double linear) => linear <= 0 ? double.NegativeInfinity : 20 * Math.Log10(linear);

    public static double FromDbfs(double dbfs) => Math.Pow(10, dbfs / 20);
}
