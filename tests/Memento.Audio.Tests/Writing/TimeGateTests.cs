using Memento.Audio.Writing;

namespace Memento.Audio.Tests.Writing;

public sealed class TimeGateTests
{
    private const int Rate = 48_000;
    private const long Ms = QpcClock.TicksPerMillisecond;

    [Fact]
    public void OpenGateKeepsTheWholePacket()
    {
        var kept = Split(new TimeGate(), 1_000 * Ms, 480);

        Assert.Equal([new FrameRange(0, 480)], kept);
    }

    [Fact]
    public void FramesBeforeTheStartAreDropped()
    {
        var gate = new TimeGate();
        gate.SetStart(1_005 * Ms);

        // Packet at 1000 ms, 10 ms long: frames from 1005 ms (index 240) on are kept.
        Assert.Equal([new FrameRange(240, 240)], Split(gate, 1_000 * Ms, 480));
    }

    [Fact]
    public void FramesAtOrAfterTheEndAreDropped()
    {
        var gate = new TimeGate();
        gate.SetEnd(1_002 * Ms);

        Assert.Equal([new FrameRange(0, 96)], Split(gate, 1_000 * Ms, 480));
        Assert.Empty(Split(gate, 1_010 * Ms, 480));
    }

    [Fact]
    public void APauseCutsAHoleOnCaptureTimeNotArrivalTime()
    {
        var gate = new TimeGate();
        gate.Pause(1_002 * Ms);
        Assert.True(gate.IsPaused);
        Assert.Equal(6 * Ms, gate.Resume(1_008 * Ms));

        Assert.Equal([new FrameRange(0, 96), new FrameRange(384, 96)], Split(gate, 1_000 * Ms, 480));
        Assert.Equal([new FrameRange(0, 480)], Split(gate, 1_010 * Ms, 480));
    }

    [Fact]
    public void AnOpenPauseDropsEverythingAfterIt()
    {
        var gate = new TimeGate();
        gate.Pause(1_005 * Ms);

        Assert.Equal([new FrameRange(0, 240)], Split(gate, 1_000 * Ms, 480));
        Assert.Empty(Split(gate, 5_000 * Ms, 480));
    }

    [Theory]
    [InlineData(0L, 0)]
    [InlineData(-5L, 0)]
    [InlineData(1L, 1)]           // just after frame 0 → frame 1 is the first at/after it
    [InlineData(208L, 1)]         // frame 1 is at 208.33 ticks
    [InlineData(209L, 2)]
    [InlineData(100_000L, 480)]   // exactly 10 ms
    [InlineData(1_000_000L, 480)] // clamped
    public void IndexMathRoundsUpToTheNextFrame(long offsetTicks, int expected)
    {
        Assert.Equal(expected, TimeGate.IndexAtOrAfter(5_000_000 + offsetTicks, 5_000_000, 480, Rate));
    }

    private static List<FrameRange> Split(TimeGate gate, long qpc, int frames)
    {
        var kept = new List<FrameRange>();
        gate.Split(qpc, frames, Rate, kept);
        return kept;
    }
}
