using Memento.Video.Encoding;
using Memento.Video.Recording;
using Memento.Video.Sources;
using Memento.Video.Tests.Fakes;

namespace Memento.Video.Tests.Recording;

public sealed class FramePacerTests
{
    private const long Origin = 1_000_000_000;
    private const long Period = 333_333; // 30 fps in 100 ns ticks

    private static (FakeVideoSource Source, FakeTrackWriter Writer) Pair() =>
        (new FakeVideoSource(VideoSourceId.Display(@"\\.\DISPLAY1"), "Display 1"),
         new FakeTrackWriter("x.mp4", new VideoEncoderSettings(1920, 1080), new H264EncoderInfo("t", true, null, null)));

    [Fact]
    public void EachDueSlotIsWrittenWithTheSlotsOwnTime()
    {
        var (source, writer) = Pair();
        var pacer = new FramePacer(Origin, 30);

        for (var k = 0; k < 5; k++)
        {
            Assert.Equal(VideoWriteResult.Written, pacer.Tick(Origin + (k * Period) + 1_000, source, writer, paused: false));
        }

        Assert.Equal([0, Period, 2 * Period, 3 * Period, 4 * Period], writer.Timestamps);
        Assert.Equal(5, pacer.FramesWritten);
        Assert.Equal(0, pacer.FramesDropped);
    }

    [Fact]
    public void NothingIsWrittenBeforeTheSlotIsDue()
    {
        var (source, writer) = Pair();
        var pacer = new FramePacer(Origin, 30);
        pacer.Tick(Origin, source, writer, paused: false);

        Assert.Null(pacer.Tick(Origin + Period - 1, source, writer, paused: false));
        Assert.Single(writer.Timestamps);
        Assert.Equal(Origin + Period, pacer.NextDueQpc);
    }

    [Fact]
    public void ALateTickWritesTheNewestSlotAndCountsTheSkippedOnesAsDropped()
    {
        var (source, writer) = Pair();
        var pacer = new FramePacer(Origin, 30);
        pacer.Tick(Origin, source, writer, paused: false);

        // The pacing thread wakes 4.5 frames late: slots 1–3 are skipped, slot 4 is written with its own time.
        pacer.Tick(Origin + (4 * Period) + (Period / 2), source, writer, paused: false);

        Assert.Equal([0, 4 * Period], writer.Timestamps);
        Assert.Equal(3, pacer.FramesDropped);
        Assert.Equal(Origin + (5 * Period), pacer.NextDueQpc);
    }

    [Fact]
    public void AFullEncoderDropsTheFrameInsteadOfWaiting()
    {
        var (source, writer) = Pair();
        var pacer = new FramePacer(Origin, 30);
        writer.Next = VideoWriteResult.NoFreeSurface;

        Assert.Equal(VideoWriteResult.NoFreeSurface, pacer.Tick(Origin, source, writer, paused: false));
        Assert.Equal(1, pacer.FramesDropped);
        Assert.Equal(0, pacer.FramesWritten);
        Assert.Equal(VideoWriteResult.Written, pacer.Tick(Origin + Period, source, writer, paused: false));
    }

    [Fact]
    public void SlotsWithoutAPictureOrWhilePausedAreIdleNotDropped()
    {
        var (source, writer) = Pair();
        source.HasPicture = false;
        var pacer = new FramePacer(Origin, 30);

        pacer.Tick(Origin, source, writer, paused: false);
        source.HasPicture = true;
        pacer.Tick(Origin + Period, source, writer, paused: true);
        pacer.Tick(Origin + (2 * Period), source, writer, paused: false);

        Assert.Equal(2, pacer.FramesIdle);
        Assert.Equal(0, pacer.FramesDropped);
        Assert.Equal([2 * Period], writer.Timestamps);
    }

    [Fact]
    public void RebaseLeavesThePausedTimeOutOfTheTrack()
    {
        var (source, writer) = Pair();
        var pacer = new FramePacer(Origin, 30);
        pacer.Tick(Origin, source, writer, paused: false);
        pacer.Tick(Origin + Period, source, writer, paused: false);

        // Paused for ten seconds, then resumed: the next frame follows the last one directly.
        var resumed = Origin + (2 * Period) + 100_000_000;
        pacer.Rebase(resumed);
        pacer.Tick(resumed, source, writer, paused: false);
        pacer.Tick(resumed + Period, source, writer, paused: false);

        Assert.Equal([0, Period, 2 * Period, 3 * Period], writer.Timestamps);
        Assert.Equal(0, pacer.FramesDropped);
    }

    [Fact]
    public void ATrackStartedMidRecordingStartsAtItsSlotOnTheTimeline()
    {
        var (source, writer) = Pair();
        var start = Origin + (90 * Period) + 10;
        var pacer = new FramePacer(Origin, 30, FramePacer.SlotAt(Origin, 30, start));

        pacer.Tick(start + Period, source, writer, paused: false);

        Assert.Equal(91 * Period, writer.Timestamps[0]);
        Assert.Equal(0, pacer.FramesDropped);
    }
}
