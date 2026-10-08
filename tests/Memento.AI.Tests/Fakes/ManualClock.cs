namespace Memento.AI.Tests.Fakes;

/// <summary>A clock whose timestamps move only when a test (or <see cref="StepPerRead"/>) moves them.</summary>
internal sealed class ManualClock : TimeProvider
{
    private long _ticks = TimeSpan.TicksPerSecond;

    /// <summary>Moves the clock on by this much every time a timestamp is read (zero: time stands still).</summary>
    public TimeSpan StepPerRead { get; set; }

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => Interlocked.Add(ref _ticks, StepPerRead.Ticks);

    public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);
}
