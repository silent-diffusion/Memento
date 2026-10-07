namespace Memento.Core.Engines;

/// <summary>
/// Decides whether heavy processing stages (transcription, speakers) may run now: not while the user paused processing,
/// not while a recording is active or the processor has been over 85% busy for 10 seconds (when "pause when busy" is on),
/// and not while the library drive is low on space. Resumes by itself when the reason goes away.
/// </summary>
public sealed class ProcessingGate
{
    /// <summary>Words for <see cref="Reason"/>; the footer shows them after "Transcription paused · ".</summary>
    public const string BusyReason = "PC is busy";
    public const string ManualReason = "Paused by you";
    public const string LowSpaceReason = "Low disk space";

    public const double BusyCpuPercent = 85;
    public static readonly TimeSpan BusyFor = TimeSpan.FromSeconds(10);

    private readonly object _sync = new();
    private readonly TimeProvider _time;
    private TaskCompletionSource _open = NewOpen();
    private bool _manual;
    private bool _recording;
    private bool _lowSpace;
    private bool _cpuBusy;
    private DateTimeOffset? _busySince;
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
    /// One sample from the busy watch. <paramref name="cpuBusyPercent"/> excludes Memento's own worker, so a
    /// transcription on the processor does not pause itself.
    /// </summary>
    public void Sample(bool pauseWhenBusy, bool recordingActive, bool lowSpace, double? cpuBusyPercent)
    {
        Update(() =>
        {
            _recording = pauseWhenBusy && recordingActive;
            _lowSpace = lowSpace;
            if (!pauseWhenBusy || cpuBusyPercent is not { } cpu || cpu <= BusyCpuPercent)
            {
                _busySince = null;
                _cpuBusy = false;
            }
            else
            {
                var now = _time.GetUtcNow();
                _busySince ??= now;
                _cpuBusy = now - _busySince.Value >= BusyFor;
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
            var reason = _manual ? ManualReason : _lowSpace ? LowSpaceReason : (_recording || _cpuBusy) ? BusyReason : null;
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
