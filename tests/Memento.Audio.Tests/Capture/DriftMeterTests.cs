using Memento.Audio.Capture;

namespace Memento.Audio.Tests.Capture;

public sealed class DriftMeterTests
{
    private const long Second = QpcClock.TicksPerSecond;

    [Theory]
    [InlineData(48_000L, 1.0, 0.0)]
    [InlineData(48_048L, 1.0, 1_000.0)]     // 48 extra frames per second = +1000 ppm
    [InlineData(47_952L, 1.0, -1_000.0)]
    [InlineData(172_800_000L, 3_600.0, 0.0)]
    [InlineData(172_801_728L, 3_600.0, 10.0)] // 1728 frames over an hour = +10 ppm
    public void ComputesPartsPerMillion(long frames, double seconds, double expectedPpm)
    {
        var ppm = DriftMeter.ComputePpm(frames, 48_000, (long)(seconds * Second));

        Assert.Equal(expectedPpm, ppm, 6);
    }

    [Fact]
    public void ZeroElapsedIsZeroNotInfinity()
    {
        Assert.Equal(0, DriftMeter.ComputePpm(480, 48_000, 0));
    }

    [Fact]
    public void MeasuresFromPacketTimestampsNotArrival()
    {
        var meter = new DriftMeter(48_000);
        var qpc = 1_000 * Second;

        // 10 s of 10 ms packets from a device whose clock is 200 ppm slow: each packet's timestamp is 10.002 ms apart.
        for (var i = 0; i < 1_000; i++)
        {
            meter.Add(qpc + (i * 100_020L), 480, timestampReliable: true);
        }

        var ppm = meter.Ppm;
        Assert.NotNull(ppm);
        Assert.Equal(-200, ppm.Value, 0);
        Assert.Equal(480_000, meter.Frames);
    }

    [Fact]
    public void UnreliablePacketsCountFramesButNotTime()
    {
        var meter = new DriftMeter(48_000);
        meter.Add(10 * Second, 48_000, timestampReliable: true);
        meter.Add(0, 48_000, timestampReliable: false);
        meter.Add(12 * Second, 48_000, timestampReliable: true);

        Assert.Equal(0, meter.Ppm!.Value, 6);
        Assert.Equal(144_000, meter.Frames);
    }

    [Fact]
    public void StaysNullUntilASecondHasPassed()
    {
        var meter = new DriftMeter(48_000);
        meter.Add(Second, 480, true);
        meter.Add(Second + (Second / 2), 480, true);

        Assert.Null(meter.Ppm);
    }
}
