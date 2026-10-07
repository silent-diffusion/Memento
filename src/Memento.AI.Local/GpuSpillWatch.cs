namespace Memento.AI.Local;

/// <summary>
/// Watches this process's shared GPU memory against a baseline taken before the model was loaded (ENGINE-NOTES.md
/// section H, trap 2): Windows does not fail an overcommitted allocation, it moves it to shared system memory and the
/// model runs 10-25x slower, also when another app takes video memory mid-run. When the growth passes the threshold
/// the watch fires <c>onSpill</c> once (the engine aborts the decode) and <see cref="Spilled"/> stays true.
/// Normal runs grow shared memory by under 100 MB; the spike's spills grew it by 0.9-4 GB.
/// </summary>
public sealed class GpuSpillWatch : IDisposable
{
    private readonly IGpuProcessMemory _memory;
    private readonly long _threshold;
    private readonly Action _onSpill;
    private readonly object _gate = new();
    private readonly long? _baselineShared;
    private Timer? _timer;
    private int _fired;

    public GpuSpillWatch(IGpuProcessMemory memory, long thresholdBytes, Action onSpill)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(onSpill);
        _memory = memory;
        _threshold = thresholdBytes;
        _onSpill = onSpill;
        _baselineShared = memory.Sample()?.SharedBytes;
    }

    /// <summary>The counters could be read; without them the watch cannot see a spill.</summary>
    public bool CanWatch => _baselineShared is not null;

    public bool Spilled => Volatile.Read(ref _fired) != 0;

    /// <summary>The largest shared-memory growth seen.</summary>
    public long MaxSharedGrowthBytes { get; private set; }

    /// <summary>Dedicated GPU memory at the latest sample.</summary>
    public long? LastDedicatedBytes { get; private set; }

    /// <summary>Checks every <paramref name="interval"/> on a timer thread.</summary>
    public void Start(TimeSpan interval)
    {
        if (!CanWatch)
        {
            return;
        }

        _timer ??= new Timer(_ => Check(), null, interval, interval);
    }

    /// <summary>Samples now; returns true when the process has spilled (now or before).</summary>
    public bool Check()
    {
        if (_baselineShared is not { } baseline)
        {
            return false;
        }

        var sample = _memory.Sample();
        if (sample is null)
        {
            return Spilled;
        }

        lock (_gate)
        {
            LastDedicatedBytes = sample.DedicatedBytes;
            var growth = sample.SharedBytes - baseline;
            if (growth > MaxSharedGrowthBytes)
            {
                MaxSharedGrowthBytes = growth;
            }

            if (growth > _threshold && Interlocked.Exchange(ref _fired, 1) == 0)
            {
                _onSpill();
            }
        }

        return Spilled;
    }

    public void Dispose() => _timer?.Dispose();
}
