using Memento.Audio.Capture;

namespace Memento.Audio.Tests.Capture;

public sealed class SilenceGapFillerTests
{
    private const int Rate = 48_000;
    private const long Ms = QpcClock.TicksPerMillisecond;
    private const long Origin = 50_000 * Ms;

    private static SilenceGapFiller NewFiller() => new(Rate, Origin, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(5));

    [Fact]
    public void IdleFillsUpToNowMinusHoldback()
    {
        var filler = NewFiller();

        Assert.Equal(0, filler.OnIdle(Origin + (50 * Ms), out _));   // still inside the holdback
        var n = filler.OnIdle(Origin + (1_100 * Ms), out var start);

        Assert.Equal(Origin, start);
        Assert.Equal(48_000, n);                                       // 1 s = 1100 ms − 100 ms holdback
        Assert.Equal(Origin + (1_000 * Ms), filler.CursorQpc);
        Assert.Equal(0, filler.OnIdle(Origin + (1_102 * Ms), out _));  // under the 5 ms minimum chunk
    }

    [Fact]
    public void RepeatedIdleTicksNeverDriftFromTheClock()
    {
        var filler = NewFiller();
        long total = 0;
        for (long t = 0; t <= 3_600_000; t += 7)                       // an hour of 7 ms ticks
        {
            total += filler.OnIdle(Origin + (100 * Ms) + (t * Ms), out _);
        }

        total += filler.OnIdle(Origin + (100 * Ms) + (3_600_000 * Ms), out _);

        Assert.Equal(3_600L * Rate, total);
        Assert.Equal(total, filler.SynthesizedFrames);
    }

    [Fact]
    public void ARealPacketAfterSilenceFillsTheRemainingGapBeforeIt()
    {
        var filler = NewFiller();
        filler.OnIdle(Origin + (500 * Ms), out _);                     // 400 ms emitted

        var adjust = filler.OnPacket(Origin + (430 * Ms), 480, timestampReliable: true);

        Assert.Equal(30 * 48, adjust.SilenceFrames);                   // 400 → 430 ms
        Assert.Equal(Origin + (400 * Ms), adjust.SilenceStartQpc);
        Assert.Equal(0, adjust.TrimFrames);
        Assert.Equal(Origin + (440 * Ms), filler.CursorQpc);
    }

    [Fact]
    public void APacketOverlappingSynthesizedSilenceIsTrimmed()
    {
        var filler = NewFiller();
        filler.OnIdle(Origin + (500 * Ms), out _);                     // cursor at 400 ms

        var adjust = filler.OnPacket(Origin + (392 * Ms), 480, timestampReliable: true);

        Assert.Equal(0, adjust.SilenceFrames);
        Assert.Equal(8 * 48, adjust.TrimFrames);
        Assert.Equal(Origin + (400 * Ms), adjust.KeptStartQpc);
        Assert.Equal(Origin + (402 * Ms), filler.CursorQpc);
    }

    [Fact]
    public void APacketEntirelyInsideEmittedSilenceIsDropped()
    {
        var filler = NewFiller();
        filler.OnIdle(Origin + (500 * Ms), out _);

        var adjust = filler.OnPacket(Origin + (300 * Ms), 480, timestampReliable: true);

        Assert.Equal(480, adjust.TrimFrames);
        Assert.Equal(Origin + (400 * Ms), filler.CursorQpc);
    }

    [Fact]
    public void ContinuousPacketsWithJitterAndDriftAreNeverAdjusted()
    {
        var filler = NewFiller();
        var rng = new Random(3);
        var qpc = Origin;
        var adjusted = 0;
        for (var i = 0; i < 360_000; i++)                              // an hour of 10 ms packets
        {
            var jitter = rng.Next(-10_000, 10_000);                    // ±1 ms timestamp jitter
            var adjust = filler.OnPacket(qpc + jitter, 480, timestampReliable: true);
            adjusted += adjust.SilenceFrames + adjust.TrimFrames;
            qpc += 100_010;                                            // device clock 100 ppm slow vs QPC
        }

        Assert.Equal(0, adjusted);
        Assert.Equal(0, filler.SynthesizedFrames);
    }

    [Fact]
    public void FirstPacketAfterStartUpLatencyGetsLeadingSilence()
    {
        var filler = NewFiller();

        var adjust = filler.OnPacket(Origin + (137 * Ms), 480, timestampReliable: true);

        Assert.Equal(137 * 48, adjust.SilenceFrames);
        Assert.Equal(Origin, adjust.SilenceStartQpc);
    }

    [Fact]
    public void UnreliableTimestampsFollowTheCursor()
    {
        var filler = NewFiller();
        filler.OnPacket(Origin, 480, timestampReliable: true);

        var adjust = filler.OnPacket(0, 480, timestampReliable: false);

        Assert.Equal(0, adjust.SilenceFrames);
        Assert.Equal(Origin + (10 * Ms), adjust.KeptStartQpc);
        Assert.Equal(Origin + (20 * Ms), filler.CursorQpc);
    }
}
