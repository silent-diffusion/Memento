using Memento.Audio.Writing;

namespace Memento.Audio.Tests.Writing;

public sealed class TrackWriterTests : IDisposable
{
    private const long Ms = QpcClock.TicksPerMillisecond;
    private readonly TempDirectory _dir = new();
    private long _now = 1_000_000 * Ms;

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void ConvertsFloatCaptureToInt24OnDisk()
    {
        using (var writer = NewWriter("mic"))
        {
            writer.Write(Signals.AsBytes(Signals.Constant(480, 2, 0.5f)), _now);
            Assert.Equal(AudioFormat.Pcm24(48_000, 2), writer.StorageFormat);
        }

        var set = WavTrackSet.Open(_dir.Path, "mic");
        Assert.Equal(AudioFormat.Pcm24(48_000, 2), set.Format);
        using var reader = set.OpenReader();
        var bytes = new byte[480 * 6];
        Assert.Equal(bytes.Length, reader.Read(bytes));
        Assert.Equal(4_194_304, PcmConverter.ReadInt24(bytes));
    }

    [Fact]
    public void PausedTimeIsExcludedAndTheGapIsReported()
    {
        var packet = Signals.AsBytes(Signals.Constant(480, 2, 0.1f)); // 10 ms
        TrackWriter writer;
        using (writer = NewWriter("mic"))
        {
            writer.SetStart(_now);
            var t = _now;
            for (var i = 0; i < 100; i++, t += 10 * Ms)
            {
                if (i == 30)
                {
                    writer.Pause(t + (5 * Ms));          // 305 ms in
                }

                if (i == 80)
                {
                    writer.Resume(t + (5 * Ms));         // 805 ms in: 500 ms pause
                }

                writer.Write(packet, t);
            }

            Assert.Equal(_now, writer.FirstFrameQpc);
            Assert.Equal(t, writer.LastFrameEndQpc);
        }

        // 1000 ms captured − 500 ms paused.
        Assert.Equal(24_000, writer.FramesWritten);
        var gap = Assert.Single(writer.Gaps);
        Assert.Equal(TimeSpan.FromMilliseconds(500), gap.Duration);
        Assert.Equal(TimeSpan.FromMilliseconds(305), gap.At);
        Assert.Equal(14_640, gap.AtFrame);
    }

    [Fact]
    public void ATimeTheDeviceDeliveredNothingIsWrittenAsSilenceSoTheTrackStaysInStep()
    {
        var packet = Signals.AsBytes(Signals.Constant(480, 2, 0.1f)); // 10 ms
        TrackWriter writer;
        using (writer = NewWriter("mic"))
        {
            writer.SetStart(_now);
            var t = _now;
            for (var i = 0; i < 50; i++, t += 10 * Ms)
            {
                writer.Write(packet, t);
            }

            t += 120_000 * Ms; // the PC slept, or Memento was frozen, for two minutes
            for (var i = 0; i < 50; i++, t += 10 * Ms)
            {
                writer.Write(packet, t);
            }

            writer.Write(packet, t + (30 * Ms)); // a little jitter is not a gap
        }

        // 0.5 s + 120 s of silence + 0.5 s + 10 ms + 30 ms of jitter kept as it was (no gap below 100 ms).
        Assert.Equal((500 + 120_000 + 500 + 10) * 48, writer.FramesWritten);
        Assert.Equal(120_000 * 48, writer.FilledGapFrames);
    }

    [Fact]
    public void APauseIsNotMistakenForAGap()
    {
        var packet = Signals.AsBytes(Signals.Constant(480, 2, 0.1f));
        TrackWriter writer;
        using (writer = NewWriter("mic"))
        {
            writer.SetStart(_now);
            writer.Write(packet, _now);
            writer.Pause(_now + (10 * Ms));
            writer.Resume(_now + (5_010 * Ms)); // the device kept delivering, but packets in a pause are dropped
            writer.Write(packet, _now + (5_010 * Ms));
        }

        Assert.Equal(20 * 48, writer.FramesWritten);
        Assert.Equal(0, writer.FilledGapFrames);
    }

    [Fact]
    public void FramesBeforeStartAndAfterEndAreNotWritten()
    {
        using var writer = NewWriter("system");
        writer.SetStart(_now + (5 * Ms));
        writer.End(_now + (25 * Ms));
        var packet = Signals.AsBytes(Signals.Constant(480, 2, 0.1f));

        writer.Write(packet, _now);
        writer.Write(packet, _now + (10 * Ms));
        writer.Write(packet, _now + (20 * Ms));
        writer.WriteSilence(480, _now + (30 * Ms));

        Assert.Equal(960, writer.FramesWritten);
        Assert.Equal(_now + (5 * Ms), writer.FirstFrameQpc);
    }

    [Fact]
    public void SilenceIsWrittenAsZeros()
    {
        using (var writer = NewWriter("system"))
        {
            writer.WriteSilence(4_800, _now);
        }

        var set = WavTrackSet.Open(_dir.Path, "system");
        Assert.Equal(4_800, set.TotalFrames);
        using var reader = set.OpenReader();
        var bytes = new byte[4_800 * 6];
        reader.Read(bytes);
        Assert.All(bytes, b => Assert.Equal(0, b));
    }

    [Fact]
    public void FlushesTheManagedBufferOncePerSecond()
    {
        using var writer = NewWriter("mic");
        var packet = Signals.AsBytes(Signals.Constant(480, 2, 0.1f)); // 2880 bytes stored, well under 64 KiB
        var path = writer.Parts[0];

        writer.Write(packet, _now);
        _now += 500 * Ms;
        writer.FlushIfDue();
        Assert.Equal(68, LengthOnDisk(path));

        _now += 600 * Ms;
        writer.FlushIfDue();
        Assert.Equal(68 + 2_880, LengthOnDisk(path));
    }

    [Fact]
    public void CheckpointReportsWhatIsOnDisk()
    {
        using var writer = NewWriter("mic");
        writer.Write(Signals.AsBytes(Signals.Constant(4_800, 2, 0.1f)), _now);

        var checkpoint = writer.Checkpoint();

        Assert.Equal(4_800, checkpoint.Frames);
        Assert.Equal(TimeSpan.FromMilliseconds(100), checkpoint.Duration);
        Assert.False(WavFileInfo.Read(checkpoint.Parts[0]).HeaderNeedsRepair);
    }

    [Fact]
    public void RollsOverThroughTheTrackWriter()
    {
        using (var writer = new TrackWriter(new TrackWriterOptions(_dir.Path, "app-player", AudioFormat.Float32Stereo48k)
        {
            RolloverBytes = 4_800 * 6,
            DurableCheckpoints = false,
            Clock = () => _now,
        }))
        {
            writer.Write(Signals.AsBytes(Signals.Constant(12_000, 2, 0.1f)), _now);
            Assert.Equal(3, writer.Parts.Count);
        }

        Assert.Equal(12_000, WavTrackSet.Open(_dir.Path, "app-player").TotalFrames);
    }

    private TrackWriter NewWriter(string stem) =>
        new(new TrackWriterOptions(_dir.Path, stem, AudioFormat.Float32Stereo48k) { DurableCheckpoints = false, Clock = () => _now });

    private static long LengthOnDisk(string path)
    {
        using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return f.Length;
    }
}
