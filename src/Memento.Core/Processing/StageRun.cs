namespace Memento.Core.Processing;

/// <summary>One run of a stage for one recording.</summary>
public sealed class StageRun(string recordingId)
{
    private int _stopReason;

    public string RecordingId { get; } = recordingId;

    /// <summary>Why the run's token was cancelled (<see cref="StageStopReason.None"/> while it runs).</summary>
    public StageStopReason StopReason => (StageStopReason)Volatile.Read(ref _stopReason);

    internal void Stop(StageStopReason reason) => Interlocked.CompareExchange(ref _stopReason, (int)reason, (int)StageStopReason.None);
}
