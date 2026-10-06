namespace Memento.Audio.Capture;

/// <summary>
/// Drift of a capture stream against the QPC clock: frames delivered between the first and the latest packet
/// with a reliable timestamp, versus the QPC time between those packets. Positive ppm means the device delivers
/// more frames than the clock says (its crystal runs fast). Reported at each checkpoint. Consumer side.
/// </summary>
public sealed class DriftMeter(int sampleRate)
{
    /// <summary>Below this span the estimate is noise and <see cref="Ppm"/> stays null.</summary>
    public static readonly TimeSpan MinimumSpan = TimeSpan.FromSeconds(1);

    private readonly object _sync = new();
    private long _firstQpc;
    private long _framesAtFirst = -1;
    private long _lastQpc;
    private long _framesAtLast;
    private long _frames;

    /// <summary>Frames seen so far (real and silent).</summary>
    public long Frames
    {
        get
        {
            lock (_sync)
            {
                return _frames;
            }
        }
    }

    /// <summary>Drift in parts per million, or null until <see cref="MinimumSpan"/> of reliable timestamps has passed.</summary>
    public double? Ppm
    {
        get
        {
            lock (_sync)
            {
                if (_framesAtFirst < 0)
                {
                    return null;
                }

                var elapsed = _lastQpc - _firstQpc;
                return elapsed < MinimumSpan.Ticks ? null : ComputePpm(_framesAtLast - _framesAtFirst, sampleRate, elapsed);
            }
        }
    }

    /// <summary>(frames / rate − elapsed) / elapsed × 10⁶, with elapsed in <see cref="QpcClock"/> ticks.</summary>
    public static double ComputePpm(long frames, int sampleRate, long elapsedTicks)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        if (elapsedTicks <= 0)
        {
            return 0;
        }

        var audioSeconds = frames / (double)sampleRate;
        var clockSeconds = elapsedTicks / (double)QpcClock.TicksPerSecond;
        return (audioSeconds - clockSeconds) / clockSeconds * 1e6;
    }

    /// <summary>Records a packet of <paramref name="frames"/> frames whose first frame was captured at <paramref name="qpc"/>.</summary>
    public void Add(long qpc, int frames, bool timestampReliable)
    {
        lock (_sync)
        {
            if (timestampReliable)
            {
                if (_framesAtFirst < 0)
                {
                    _firstQpc = qpc;
                    _framesAtFirst = _frames;
                }

                _lastQpc = qpc;
                _framesAtLast = _frames;
            }

            _frames += frames;
        }
    }
}
