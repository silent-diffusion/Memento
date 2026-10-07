using System.Collections.Concurrent;

namespace Memento.AI.Tests.Fakes;

/// <summary>Real clock, but every timer fires at once; records the delays that were asked for (retry waits).</summary>
internal sealed class InstantTimeProvider : TimeProvider
{
    private readonly ConcurrentQueue<TimeSpan> _delays = new();

    public IReadOnlyList<TimeSpan> Delays => [.. _delays];

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        if (dueTime != Timeout.InfiniteTimeSpan)
        {
            _delays.Enqueue(dueTime);
            dueTime = TimeSpan.Zero;
        }

        return System.CreateTimer(callback, state, dueTime, period);
    }
}
