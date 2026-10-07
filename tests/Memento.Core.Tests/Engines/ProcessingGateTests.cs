using Memento.Core.Engines;
using Memento.Core.Tests.Fakes;

namespace Memento.Core.Tests.Engines;

public sealed class ProcessingGateTests
{
    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 10, 6, 10, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task APauseByTheUserHoldsUntilResumed()
    {
        var gate = new ProcessingGate(_time);
        var changes = 0;
        gate.Changed += (_, _) => changes++;

        gate.SetManual(true);
        var open = gate.WhenOpenAsync(CancellationToken.None);

        Assert.Equal(ProcessingGate.ManualReason, gate.Reason);
        Assert.False(open.IsCompleted);
        gate.SetManual(false);
        await open.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Null(gate.Reason);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void ARecordingPausesOnlyWhenPauseWhenBusyIsOn()
    {
        var gate = new ProcessingGate(_time);

        gate.Sample(pauseWhenBusy: false, recordingActive: true, lowSpace: false, cpuBusyPercent: 10);
        Assert.Null(gate.Reason);

        gate.Sample(pauseWhenBusy: true, recordingActive: true, lowSpace: false, cpuBusyPercent: 10);
        Assert.Equal(ProcessingGate.BusyReason, gate.Reason);

        gate.Sample(pauseWhenBusy: true, recordingActive: false, lowSpace: false, cpuBusyPercent: 10);
        Assert.Null(gate.Reason);
    }

    [Fact]
    public void TheProcessorMustBeBusyForTenSeconds()
    {
        var gate = new ProcessingGate(_time);

        gate.Sample(true, false, false, 95);
        _time.Advance(TimeSpan.FromSeconds(9));
        gate.Sample(true, false, false, 95);
        Assert.Null(gate.Reason);

        _time.Advance(TimeSpan.FromSeconds(1));
        gate.Sample(true, false, false, 90);
        Assert.Equal(ProcessingGate.BusyReason, gate.Reason);

        // It resumes once the processor has been calm (70 % or less) for 15 s; then the watch starts over.
        gate.Sample(true, false, false, 40);
        Assert.Equal(ProcessingGate.BusyReason, gate.Reason);
        _time.Advance(TimeSpan.FromSeconds(15));
        gate.Sample(true, false, false, 40);
        Assert.Null(gate.Reason);
        _time.Advance(TimeSpan.FromSeconds(30));
        gate.Sample(true, false, false, 99);
        Assert.Null(gate.Reason);
    }

    [Fact]
    public void AProcessorHoveringAroundTheThresholdDoesNotStopAndStartTheStageAgainAndAgain()
    {
        var gate = new ProcessingGate(_time);
        var changes = 0;
        gate.Changed += (_, _) => changes++;

        // 95 % for 10 s pauses; then it swings between 80 and 90 % for two minutes.
        gate.Sample(true, false, false, 95);
        _time.Advance(TimeSpan.FromSeconds(10));
        gate.Sample(true, false, false, 95);
        for (var i = 0; i < 120; i++)
        {
            _time.Advance(TimeSpan.FromSeconds(1));
            gate.Sample(true, false, false, i % 2 == 0 ? 80 : 90);
        }

        Assert.Equal(ProcessingGate.BusyReason, gate.Reason);
        Assert.Equal(1, changes);

        // A calm spell shorter than 15 s does not resume either.
        for (var i = 0; i < 10; i++)
        {
            _time.Advance(TimeSpan.FromSeconds(1));
            gate.Sample(true, false, false, 30);
        }

        gate.Sample(true, false, false, 75);
        Assert.Equal(ProcessingGate.BusyReason, gate.Reason);
    }

    [Fact]
    public void ResumeReleasesABusyPauseUntilTheNextDetection()
    {
        var gate = new ProcessingGate(_time);
        gate.Sample(true, recordingActive: true, false, 10);
        Assert.Equal(ProcessingGate.BusyReason, gate.Reason);

        gate.Resume();
        gate.Sample(true, recordingActive: true, false, 10);
        Assert.Null(gate.Reason);

        // The recording ends, and the next one pauses again.
        gate.Sample(true, recordingActive: false, false, 10);
        gate.Sample(true, recordingActive: true, false, 10);
        Assert.Equal(ProcessingGate.BusyReason, gate.Reason);
    }

    [Fact]
    public void ResumeDoesNotOverrideLowSpace()
    {
        var gate = new ProcessingGate(_time);
        gate.Sample(true, false, lowSpace: true, 10);

        gate.Resume();

        Assert.Equal(ProcessingGate.LowSpaceReason, gate.Reason);
    }

    [Fact]
    public void LowSpaceWinsOverBusyAndTheUserWinsOverBoth()
    {
        var gate = new ProcessingGate(_time);

        gate.Sample(true, true, lowSpace: true, 10);
        Assert.Equal(ProcessingGate.LowSpaceReason, gate.Reason);

        gate.SetManual(true);
        Assert.Equal(ProcessingGate.ManualReason, gate.Reason);
    }
}
