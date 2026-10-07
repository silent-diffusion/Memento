namespace Memento.Core.Export;

/// <summary>Lets a progress event through at most every <see cref="Interval"/> (four per second); final states always pass.</summary>
public sealed class ProgressThrottle(TimeProvider time)
{
    public static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(250);

    private long _last = long.MinValue;

    /// <summary><c>true</c> when an event may be sent now (and records that it was).</summary>
    public bool TryPass(bool force = false)
    {
        var now = time.GetTimestamp();
        var last = Interlocked.Read(ref _last);
        if (!force && last != long.MinValue && time.GetElapsedTime(last, now) < Interval)
        {
            return false;
        }

        Interlocked.Exchange(ref _last, now);
        return true;
    }
}
