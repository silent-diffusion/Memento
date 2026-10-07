namespace Memento.Core.Engines;

/// <summary>
/// Decides whether heavy processing stages (transcription, speakers) may run now: not while the user paused processing,
/// not while a recording is active or the processor has been over 85% busy for 10 seconds (when "pause when busy" is on;
/// a stage on the graphics card is exempt from the processor check, see <see cref="SetHeavyOnGpu"/>), and not while the
/// library drive is low on space. Resumes by itself when the reason goes away.
/// </summary>
public sealed class ProcessingGate
{
    /// <summary>Words for <see cref="Reason"/>; the footer shows them after "Transcription paused · ".</summary>
    public const string BusyReason = "PC is busy";
    public const string ManualReason = "Paused by you";
    public const string LowSpaceReason = "Low disk space";

    public const double BusyCpuPercent = 85;
    public static readonly TimeSpan BusyFor = TimeSpan.FromSeconds(10);

    /// <summary>A busy pause ends once the processor has been at or below this for <see cref="CalmFor"/>.</summary>
    public const double ResumeCpuPercent = 70;

    public static readonly TimeSpan CalmFor = TimeSpan.FromSeconds(15);

    private readonly object _sync = new();
    private readonly TimeProvider _time;
    private TaskCompletionSource _open = NewOpen();
    private bool _manual;
    private bool _recording;
    private bool _lowSpace;
    private bool _cpuBusy;
    private bool _busyReleased;
    private bool _heavyOnGpu;
    private DateTimeOffset? _busySince;
    private DateTimeOffset? _calmSince;
    private string? _reason;

    public ProcessingGate(TimeProvider time)
    {
        _time = time;
    }

    /// <summary>Raised when <see cref="Reason"/> changes (on the thread that changed it).</summary>
    public event EventHandler? Changed;

    /// <summary>Why heavy stages are paused, or <c>null</c> when they may run.</summary>
    public string? Reason
    {
        get
        {
            lock (_sync)
            {
                return _reason;
            }
        }
    }

    public bool IsPaused => Reason is not null;

    public bool ManuallyPaused
    {
        get
        {
            lock (_sync)
            {
                return _manual;
            }
        }
    }

    /// <summary>Completes when heavy stages may run.</summary>
    public Task WhenOpenAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            return _open.Task.WaitAsync(cancellationToken);
        }
    }

    /// <summary><c>processing.pause</c> / <c>processing.resume</c>.</summary>
    public void SetManual(bool paused) => Update(() => _manual = paused);

    /// <summary>
    /// Whether the heavy stage that runs (or is about to) uses the graphics card. A pass on the GPU barely loads the
    /// processor, so a busy processor does not pause it; a recording (while "Pause when busy" is on), low disk space
    /// and a pause by the user still do.
    /// </summary>
    public void SetHeavyOnGpu(bool onGpu) => Update(() => _heavyOnGpu = onGpu);

    /// <summary>The value last given to <see cref="SetHeavyOnGpu"/>.</summary>
    public bool HeavyOnGpu
    {
        get
        {
            lock (_sync)
            {
                return _heavyOnGpu;
            }
        }
    }

    /// <summary>
    /// <c>processing.resume</c> (BRIDGE.md M2 clarification 4): lifts a pause by the user and also releases a "PC is busy"
    /// pause that is in effect now, until the busy condition ends and is detected again. Low disk space still pauses.
    /// </summary>
    public void Resume() => Update(() =>
    {
        _manual = false;
        _busyReleased = _recording || _cpuBusy;
    });

    /// <summary>
    /// One sample from the busy watch. <paramref name="cpuBusyPercent"/> excludes Memento's own worker, so a
    /// transcription on the processor does not pause itself.
    /// </summary>
    public void Sample(bool pauseWhenBusy, bool recordingActive, bool lowSpace, double? cpuBusyPercent)
    {
        Update(() =>
        {
            _recording = pauseWhenBusy && recordingActive;
            _lowSpace = lowSpace;
            var now = _time.GetUtcNow();
            if (!pauseWhenBusy || cpuBusyPercent is not { } cpu)
            {
                _busySince = null;
                _calmSince = null;
                _cpuBusy = false;
            }
            else if (_cpuBusy)
            {
                // Paused for a busy processor: resume only once it has been calm for a while, or a PC hovering
                // around the threshold stops and restarts the same window again and again.
                if (cpu > ResumeCpuPercent)
                {
                    _calmSince = null;
                }
                else
                {
                    _calmSince ??= now;
                    if (now - _calmSince.Value >= CalmFor)
                    {
                        _cpuBusy = false;
                        _busySince = null;
                        _calmSince = null;
                    }
                }
            }
            else if (cpu <= BusyCpuPercent)
            {
                _busySince = null;
            }
            else
            {
                _busySince ??= now;
                _cpuBusy = now - _busySince.Value >= BusyFor;
            }

            if (!_recording && !_cpuBusy)
            {
                // The busy condition is over: the next detection pauses again.
                _busyReleased = false;
            }
        });
    }

    private static TaskCompletionSource NewOpen()
    {
        var open = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        open.SetResult();
        return open;
    }

    private void Update(Action change)
    {
        bool changed;
        lock (_sync)
        {
            change();
            var busy = _recording || (_cpuBusy && !_heavyOnGpu);
            var reason = _manual ? ManualReason : _lowSpace ? LowSpaceReason : busy && !_busyReleased ? BusyReason : null;
            changed = reason != _reason;
            _reason = reason;
            if (reason is null)
            {
                _open.TrySetResult();
            }
            else if (_open.Task.IsCompleted)
            {
                _open = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
