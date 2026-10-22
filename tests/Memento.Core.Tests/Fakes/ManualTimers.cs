namespace Memento.Core.Tests.Fakes;

/// <summary>
/// A clock whose timers fire only when the test moves it (<see cref="Advance"/>), so a timeout under test cannot fire
/// because the runner stalled. One-shot and periodic timers; callbacks run on the thread that advances.
/// </summary>
internal sealed class ManualTimers : TimeProvider
{
    private readonly object _sync = new();
    private readonly List<Timer> _timers = [];
    private TaskCompletionSource _timerCreated = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _created;
    private long _ticks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp()
    {
        lock (_sync)
        {
            return _ticks;
        }
    }

    public override DateTimeOffset GetUtcNow() => new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddTicks(GetTimestamp());

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new Timer(this, callback, state);
        timer.Change(dueTime, period);
        TaskCompletionSource created;
        lock (_sync)
        {
            _created++;
            created = _timerCreated;
            _timerCreated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        created.TrySetResult();
        return timer;
    }

    /// <summary>How many timers have been created so far.</summary>
    public int Created
    {
        get
        {
            lock (_sync)
            {
                return _created;
            }
        }
    }

    /// <summary>Completes once <paramref name="count"/> timers have been created in all (a wait under test has started).</summary>
    public async Task WaitForTimersAsync(int count)
    {
        while (true)
        {
            Task next;
            lock (_sync)
            {
                if (_created >= count)
                {
                    return;
                }

                next = _timerCreated.Task;
            }

            await next.WaitAsync(Patience.Ceiling);
        }
    }

    /// <summary>Moves the clock and fires every timer that falls due, in order.</summary>
    public void Advance(TimeSpan by)
    {
        long target;
        lock (_sync)
        {
            target = _ticks + by.Ticks;
        }

        while (true)
        {
            Timer? next;
            lock (_sync)
            {
                next = _timers.Where(t => t.Due <= target).MinBy(t => t.Due);
                if (next is null)
                {
                    _ticks = target;
                    return;
                }

                _ticks = Math.Max(_ticks, next.Due);
                if (next.Period > 0)
                {
                    next.Due += next.Period;
                }
                else
                {
                    _timers.Remove(next);
                }
            }

            next.Fire();
        }
    }

    private sealed class Timer(ManualTimers owner, TimerCallback callback, object? state) : ITimer
    {
        public long Due { get; set; }

        public long Period { get; private set; }

        public void Fire() => callback(state);

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner._sync)
            {
                owner._timers.Remove(this);
                if (dueTime == Timeout.InfiniteTimeSpan)
                {
                    return true;
                }

                Due = owner._ticks + dueTime.Ticks;
                Period = period == Timeout.InfiniteTimeSpan ? 0 : period.Ticks;
                owner._timers.Add(this);
                return true;
            }
        }

        public void Dispose()
        {
            lock (owner._sync)
            {
                owner._timers.Remove(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
