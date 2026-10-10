using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Memento.Audio;
using Memento.Video.Encoding;
using Memento.Video.Gpu;
using Memento.Video.Recording;
using Memento.Video.Recovery;
using Memento.Video.Sources;
using Memento.Video.Tracks;

namespace Memento.Video.Tests.Hardware;

/// <summary>
/// Real recordings through the production session (source → pacing thread → H.264 fragmented MP4). The files go to a
/// temporary folder and are deleted afterwards; nothing is kept or sent anywhere.
/// </summary>
[Collection("Video hardware")]
public sealed partial class VideoHardwareTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "memento-video-hw-" + Guid.NewGuid().ToString("N"));

    public VideoHardwareTests()
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

    private static async Task<VideoTrack> Record(VideoDevice device, IVideoSource source, string folder, string trackId, VideoEncoderSettings settings, TimeSpan length)
    {
        var options = new VideoSessionOptions(trackId, trackId == "camera" ? "camera" : "screen", folder, settings, QpcClock.Now);
        await using var session = new VideoCaptureSession(options, source, new Mp4TrackWriterFactory(device));
        await session.StartAsync(CancellationToken.None);
        var checkpoints = 0;
        var end = DateTime.UtcNow + length;
        while (DateTime.UtcNow < end)
        {
            await Task.Delay(TimeSpan.FromSeconds(5));
            session.Checkpoint();
            checkpoints++;
        }

        Assert.True(checkpoints >= 1);
        return await session.StopAsync(CancellationToken.None);
    }

    private static void AssertPlayableTrack(string folder, VideoTrack track, double seconds)
    {
        var path = Path.Combine(folder, track.File);
        var summary = Mp4FileSummary.Read(path);
        Assert.True(summary.IsFragmented, "the capture file is fragmented MP4");
        Assert.True(summary.HasIndex, "a clean stop writes the mfra index");
        Assert.Equal(0, summary.TornBytes);
        Assert.InRange(summary.DurationSeconds, seconds - 1.5, seconds + 0.5);
        Assert.InRange(summary.Fragments.Max(f => f.Duration) / (double)summary.Timescale, 0.01, 2.1);
        Assert.Equal(track.FramesWritten, summary.Samples);
        Assert.True(track.FramesDropped <= track.FramesWritten / 10, $"{track.FramesDropped} of {track.FramesWritten} frames dropped");
    }

    [Trait("Category", "Hardware")]
    [VideoHardwareFact]
    public async Task ThirtySecondsOfTheDesktopBecomeAPlayableFragmentedMp4()
    {
        var display = VideoSourceEnumerator.ListDisplays()[0];
        using var device = VideoDevice.Create(VideoDevice.AdapterForDisplay(display.Id.Key));
        var source = GraphicsCaptureSource.ForDisplay(device, display.Id, display.Name);
        var (w, h) = VideoEncoderSettings.EncodableSize(display.Width, display.Height);

        var track = await Record(device, source, _folder, "screen", new VideoEncoderSettings(w, h), TimeSpan.FromSeconds(30));

        AssertPlayableTrack(_folder, track, 30);
        Assert.InRange(track.FramesWritten, 800, 905);
        Assert.Equal("display", track.SourceKind);
        Assert.False(string.IsNullOrEmpty(track.Encoder));

        // The remux used by finalize and recovery reads every frame back through Media Foundation.
        var remuxed = Path.Combine(_folder, "screen.remux.mp4");
        var result = await Mp4Remuxer.RemuxAsync([new Mp4Remuxer.Part(Path.Combine(_folder, track.File), 0)], remuxed, CancellationToken.None);
        Assert.Equal(track.FramesWritten, result.Samples);
        Assert.False(Mp4FileSummary.Read(remuxed).IsFragmented);
    }

    [Trait("Category", "Hardware")]
    [VideoHardwareFact(needsCamera: true)]
    public async Task ThirtySecondsOfTheCameraBecomeAPlayableFragmentedMp4()
    {
        var camera = VideoSourceEnumerator.ListCameras()[0];
        using var device = VideoDevice.Create();
        var source = CameraCaptureSource.ForCamera(device, camera.Id, camera.Name);

        var track = await Record(device, source, _folder, "camera", new VideoEncoderSettings(1280, 720), TimeSpan.FromSeconds(30));

        AssertPlayableTrack(_folder, track, 30);
        Assert.Equal("camera", track.Role);
    }

    [Trait("Category", "Hardware")]
    [VideoHardwareFact]
    public async Task AClosedWindowEndsItsTrackAndKeepsEverythingBefore()
    {
        using var window = new TestWindow(protect: false);
        using var device = VideoDevice.Create();
        var source = GraphicsCaptureSource.ForWindow(device, VideoSourceId.Window(window.Handle), "Test window");
        var options = new VideoSessionOptions("screen", "screen", _folder, new VideoEncoderSettings(640, 480), QpcClock.Now);
        await using var session = new VideoCaptureSession(options, source, new Mp4TrackWriterFactory(device));
        var problem = new TaskCompletionSource<VideoSessionEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Problem += (_, e) => problem.TrySetResult(e);
        await session.StartAsync(CancellationToken.None);
        await Task.Delay(3000);

        window.Close();
        var e = await problem.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var track = await session.StopAsync(CancellationToken.None);

        Assert.Equal(VideoSessionState.Ended, e.State);
        Assert.Equal(VideoErrorCode.SourceLost, e.Error.Code);
        Assert.StartsWith("“Test window” was closed at 0:0", e.Error.Message, StringComparison.Ordinal);
        Assert.Equal("sourceLost", track.EndReason);
        Assert.InRange(Mp4FileSummary.Read(Path.Combine(_folder, track.File)).DurationSeconds, 2.0, 4.5);
    }

    [Trait("Category", "Hardware")]
    [VideoHardwareFact]
    public void AWindowHiddenFromCaptureIsRefusedAsProtected()
    {
        using var window = new TestWindow(protect: true);
        using var device = VideoDevice.Create();

        var ex = Assert.Throws<VideoCaptureException>(() => GraphicsCaptureSource.ForWindow(device, VideoSourceId.Window(window.Handle), "Player"));

        Assert.Equal(VideoErrorCode.ProtectedContent, ex.Code);
        Assert.Contains(VideoSourceEnumerator.ListWindows(Environment.ProcessId), w => w.Id == VideoSourceId.Window(window.Handle) && w.IsExcluded);
    }

    [Trait("Category", "Hardware")]
    [VideoHardwareFact]
    public void ThereIsAlwaysAnEncoderToFallBackTo() =>
        Assert.Contains(H264EncoderProbe.List(), e => !e.IsHardware);

    /// <summary>A small window on its own UI thread; with <c>protect</c> it asks Windows to keep it out of captures.</summary>
    private sealed partial class TestWindow : IDisposable
    {
        private readonly Thread _thread;
        private Form? _form;

        public TestWindow(bool protect)
        {
            using var ready = new ManualResetEventSlim();
            _thread = new Thread(() =>
            {
                _form = new Form { Text = "Test window", Bounds = new Rectangle(80, 80, 400, 300), StartPosition = FormStartPosition.Manual, ShowInTaskbar = false };
                _form.Shown += (_, _) =>
                {
                    if (protect)
                    {
                        _ = SetWindowDisplayAffinity(_form.Handle, 0x11);
                    }

                    ready.Set();
                };
                Application.Run(_form);
            });
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.IsBackground = true;
            _thread.Start();
            ready.Wait(TimeSpan.FromSeconds(10));
            Handle = (IntPtr)_form!.Invoke(() => _form.Handle);
        }

        public IntPtr Handle { get; }

        public void Close() => _form?.Invoke(() => _form.Close());

        public void Dispose()
        {
            try
            {
                _form?.Invoke(() => _form.Close());
            }
            catch (InvalidOperationException)
            {
                // Already closed.
            }

            _thread.Join(TimeSpan.FromSeconds(5));
        }

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
    }
}
