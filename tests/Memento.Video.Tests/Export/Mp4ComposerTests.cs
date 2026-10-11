using Memento.Core.Video;
using Memento.Video.Export;
using Memento.Video.Gpu;
using Vortice.MediaFoundation;

namespace Memento.Video.Tests.Export;

/// <summary>
/// The export's video files (DESIGN.md §18.4) from small synthetic MP4s: Media Foundation's software codecs only, so
/// these run on any Windows with the Media Feature Pack. Files go to a temporary folder and are deleted.
/// </summary>
public sealed class Mp4ComposerTests : IDisposable
{
    private const int Width = 320;
    private const int Height = 180;
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "memento-compose-" + Guid.NewGuid().ToString("N"));

    public Mp4ComposerTests()
    {
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // A file still closing; the temp folder is cleaned by Windows later.
        }
    }

    private string File(string name) => Path.Combine(_folder, name);

    [Fact]
    public async Task OneFileBurnsTheInsetInWhereItWasPutsTheScreenOnTheTimelineAndKeepsTheMix()
    {
        var screen = SyntheticMedia.Video(File("screen.mp4"), Width, Height, 30, 60, _ => 200);
        var camera = SyntheticMedia.Video(File("camera.mp4"), 160, 120, 30, 90, _ => 60);
        var mix = SyntheticMedia.Wav(File("mix.wav"), 3);
        var request = new VideoComposeRequest(
            File("out.mp4"),
            new VideoComposeInput(screen, 500, 2000, Width, Height, 30, 1_000_000),
            new VideoComposeInput(camera, 0, 3000, 160, 120, 30, 500_000),
            mix,
            3000,
            Reencode: true,
            [new VideoPipChange(0, true, VideoPipChange.BottomRight), new VideoPipChange(1500, false, VideoPipChange.BottomRight)]);

        var result = await Mp4Composer.ComposeAsync(request, null, CancellationToken.None);

        Assert.Equal(90, result.Frames);
        var (x, y, w, h) = Nv12Frame.InsetRectangle(Width, Height, 160, 120, VideoPipChange.BottomRight);
        var main = SyntheticMedia.Luma(request.OutputPath, Width, Height, (16, 16, 64, 64));
        var inset = SyntheticMedia.Luma(request.OutputPath, Width, Height, (x + 8, y + 8, w - 16, h - 16));
        Assert.InRange(main.Count, 88, 92);
        double At(List<(long Time, double Luma)> frames, double seconds) => frames.First(f => f.Time >= seconds * TimeSpan.TicksPerSecond).Luma;

        Assert.InRange(At(main, 0.2), 10, 24);       // before the screen's first frame: black
        Assert.InRange(At(main, 1.0), 192, 208);     // the screen
        Assert.InRange(At(inset, 1.0), 52, 68);      // the camera inset, bottom right
        Assert.InRange(At(inset, 2.0), 192, 208);    // inset turned off at 1.5 s: the screen shows there
        Assert.InRange(At(main, 2.8), 10, 24);       // the screen track ended at 2.5 s: black
        Assert.InRange(AudioSeconds(request.OutputPath), 2.9, 3.1);
        Assert.False(System.IO.File.Exists(request.OutputPath + ".tmp"));
    }

    [Fact]
    public async Task OriginalContainerCopiesTheRecordedFramesUntouchedWithTheMix()
    {
        var screen = SyntheticMedia.Video(File("screen.mp4"), Width, Height, 30, 45, i => (byte)(i < 15 ? 80 : 180));
        var mix = SyntheticMedia.Wav(File("mix.wav"), 1.5);
        var request = new VideoComposeRequest(File("copy.mp4"), new VideoComposeInput(screen, 0, 1500, Width, Height, 30, 1_000_000), null, mix, 1500, Reencode: false, []);

        var result = await Mp4Composer.ComposeAsync(request, null, CancellationToken.None);

        Assert.Equal(45, result.Frames);
        var frames = SyntheticMedia.Luma(request.OutputPath, Width, Height, (0, 0, Width, Height));
        Assert.Equal(45, frames.Count);
        Assert.InRange(frames[5].Luma, 74, 86);
        Assert.InRange(frames[30].Luma, 174, 186);
        Assert.InRange(AudioSeconds(request.OutputPath), 1.4, 1.6);
    }

    [Fact]
    public async Task OriginalContainerKeepsATrackThatStartedLaterInItsPlaceAgainstTheAudio()
    {
        var screen = SyntheticMedia.Video(File("late.mp4"), Width, Height, 30, 30, _ => 150);
        var mix = SyntheticMedia.Wav(File("mix.wav"), 2);
        var request = new VideoComposeRequest(File("late-copy.mp4"), new VideoComposeInput(screen, 700, 1000, Width, Height, 30, 1_000_000), null, mix, 2000, Reencode: false, []);

        await Mp4Composer.ComposeAsync(request, null, CancellationToken.None);

        var frames = SyntheticMedia.Luma(request.OutputPath, Width, Height, (0, 0, Width, Height));
        Assert.Equal(30, frames.Count);
        Assert.InRange(frames[0].Time / (double)TimeSpan.TicksPerSecond, 0.65, 0.75);
    }

    [Fact]
    public async Task ACancelledComposeLeavesNoFileBehind()
    {
        var screen = SyntheticMedia.Video(File("screen.mp4"), Width, Height, 30, 30, _ => 100);
        using var cancel = new CancellationTokenSource();
        var progress = new Progress<double>(_ => cancel.Cancel());
        var request = new VideoComposeRequest(File("cancelled.mp4"), new VideoComposeInput(screen, 0, 1000, Width, Height, 30, 1_000_000), null, null, 60_000, Reencode: true, []);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Mp4Composer.ComposeAsync(request, new SyncProgress(_ => cancel.Cancel()), cancel.Token));

        Assert.False(System.IO.File.Exists(request.OutputPath));
        Assert.False(System.IO.File.Exists(request.OutputPath + ".tmp"));
        GC.KeepAlive(progress);
    }

    [Fact]
    public void TheInsetMovesWithTheLastChangeBeforeEachFrame()
    {
        VideoPipChange[] pip = [new(0, true, VideoPipChange.TopLeft), new(1000, true, VideoPipChange.BottomLeft), new(2000, false, VideoPipChange.BottomLeft)];

        Assert.Equal(VideoPipChange.TopLeft, Mp4Composer.PipAt(pip, 999).Corner);
        Assert.Equal(VideoPipChange.BottomLeft, Mp4Composer.PipAt(pip, 1000).Corner);
        Assert.False(Mp4Composer.PipAt(pip, 2500).Enabled);
        Assert.True(Mp4Composer.PipAt([], 10).Enabled);
        Assert.Equal((1856, 64, 640, 360), Nv12Frame.InsetRectangle(2560, 1440, 1280, 720, VideoPipChange.TopRight));
    }

    /// <summary>The length of the file's audio track in seconds (0 without one).</summary>
    internal static double AudioSeconds(string path)
    {
        MediaFoundationRuntime.EnsureStarted();
        using var reader = MediaFactory.MFCreateSourceReaderFromURL(path, null);
        reader.SetStreamSelection(SourceReaderIndex.AllStreams, false);
        reader.SetStreamSelection(SourceReaderIndex.FirstAudioStream, true);
        long end = 0;
        while (true)
        {
            using var sample = reader.ReadSample(SourceReaderIndex.FirstAudioStream, SourceReaderControlFlag.None, out _, out var flags, out var time);
            if ((flags & SourceReaderFlag.EndOfStream) != 0 || sample is null)
            {
                break;
            }

            end = Math.Max(end, time + sample.SampleDuration);
        }

        return end / (double)TimeSpan.TicksPerSecond;
    }

    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
