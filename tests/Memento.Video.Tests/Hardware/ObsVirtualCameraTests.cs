using System.Diagnostics;
using Memento.Audio;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Processing;
using Memento.Core.Projects;
using Memento.Core.Recording.Simulation;
using Memento.Video.Encoding;
using Memento.Video.Export;
using Memento.Video.Gpu;
using Memento.Video.Hosting;
using Memento.Video.Recording;
using Memento.Video.Recovery;
using Memento.Video.Sources;
using Xunit.Abstractions;

namespace Memento.Video.Tests.Hardware;

/// <summary>
/// The OBS Virtual Camera, a DirectShow-only camera, through the production paths (ENGINE-NOTES.md §R): listed once
/// with its backend, a thumbnail, 20 s recorded through the coordinator beside simulated audio, and the track ending
/// cleanly when OBS stops sending. The tests use the virtual camera as OBS has it running; they never start or stop
/// OBS (the stop is simulated at the watch). Files go to a temporary folder and are deleted.
/// </summary>
[Collection("Video hardware")]
public sealed class ObsVirtualCameraTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "memento-video-obs-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        for (var attempt = 0; attempt < 5 && Directory.Exists(_folder); attempt++)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(_folder, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                Directory.Delete(_folder, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(500);
            }
        }
    }

    [Trait("Category", "Hardware")]
    [ObsVirtualCameraFact]
    public void TheVirtualCameraIsListedOnceThroughDirectShow()
    {
        var cameras = VideoSourceEnumerator.ListCameras();

        var camera = Assert.Single(cameras, c => c.Name == ObsVirtualCamera.Name);
        Assert.Equal(CameraBackend.DirectShow, camera.Backend);
        Assert.Equal("camera:dshow:clsid:{A3FCE0F5-3493-419F-958A-ABA1250EC20B}", camera.Id.ToString());
        Assert.DoesNotContain(VideoSourceEnumerator.ListMediaFoundationCameras(), c => c.Name == ObsVirtualCamera.Name);
    }

    [Trait("Category", "Hardware")]
    [ObsVirtualCameraFact]
    public async Task ItsThumbnailIsAJpegAndTheCameraStaysOpenBetweenRequests()
    {
        var camera = ObsVirtualCamera.Find()!;
        var capture = new MediaFoundationVideoCapture();

        var clock = Stopwatch.StartNew();
        var first = await capture.ThumbnailAsync(camera.Id.ToString(), 320, CancellationToken.None);
        var firstMs = clock.ElapsedMilliseconds;
        clock.Restart();
        var second = await capture.ThumbnailAsync(camera.Id.ToString(), 320, CancellationToken.None);
        var secondMs = clock.ElapsedMilliseconds;
        output.WriteLine($"thumbnail {first.Width}x{first.Height}: first {firstMs} ms (opens the graph), second {secondMs} ms");

        Assert.StartsWith("data:image/jpeg;base64,", first.DataUrl, StringComparison.Ordinal);
        Assert.Equal(320, first.Width);
        Assert.Equal(180, first.Height);
        Assert.True(secondMs < 500, $"the second thumbnail took {secondMs} ms");
        Assert.StartsWith("data:image/jpeg;base64,", second.DataUrl, StringComparison.Ordinal);
        await Task.Delay(CameraStills.IdleAfter + TimeSpan.FromSeconds(4));
    }

    [Trait("Category", "Hardware")]
    [ObsVirtualCameraFact]
    public async Task TwentySecondsOfTheVirtualCameraRecordBesideTheAudio()
    {
        var camera = ObsVirtualCamera.Find()!;
        using var host = new OrchestratorHost(_folder);
        await host.Get<Core.Settings.ISettingsStore>().LoadAsync(CancellationToken.None);
        await host.Get<Core.Settings.ISettingsStore>().UpdateAsync(s => s with { Recording = s.Recording with { CheckpointSeconds = 5 } }, CancellationToken.None);
        await host.Get<Core.Library.LibraryOpener>().OpenAsync(CancellationToken.None);
        var cpu = Process.GetCurrentProcess().TotalProcessorTime;
        var wall = Stopwatch.StartNew();

        var started = await host.Recordings.StartAsync(
            new RecordingStartParams { Title = "Virtual camera check", Type = "meeting", SourceIds = [SimulatedAudioSourceProvider.Microphone.Id, camera.Id.ToString()] },
            CancellationToken.None);
        await Task.Delay(TimeSpan.FromSeconds(20));
        var during = host.Recordings.Current!.VideoTracks;
        await host.Recordings.StopAsync(started.SessionId, CancellationToken.None);
        var cpuShare = (Process.GetCurrentProcess().TotalProcessorTime - cpu).TotalMilliseconds / wall.Elapsed.TotalMilliseconds / Environment.ProcessorCount * 100;
        await host.Recordings.WhenIdleAsync();
        await host.Processing.WhenIdleAsync();

        var manifest = await host.Store.LoadAsync(started.RecordingId, CancellationToken.None);
        var folder = host.Store.GetProjectFolder(started.RecordingId);
        var track = Assert.Single(manifest.VideoTracks);
        var summary = Mp4FileSummary.Read(Path.Combine(folder, track.File));
        output.WriteLine($"camera {track.Width}x{track.Height}@{track.FrameRate} {track.Encoder} (hardware {track.HardwareEncoder}); starts at {track.StartOffsetMs} ms, {track.DurationMs} ms, {track.FramesWritten} frames, {track.FramesDropped} dropped; process {cpuShare:0.0} % of the PC (simulated audio, camera, encoder, video stage)");

        var status = Assert.Single(during);
        Assert.Equal(("camera", "recording"), (status.TrackId, status.State));
        Assert.Equal(ProjectStates.Ready, manifest.State);
        Assert.Equal(("camera", "camera", camera.Id.ToString()), (track.Role, track.SourceKind, track.SourceId));
        Assert.InRange(track.DurationMs, 17_000, 21_000);
        Assert.True(track.FramesDropped <= track.FramesWritten / 10, $"{track.FramesDropped} of {track.FramesWritten} frames dropped");
        Assert.True(summary.IsFragmented && summary.HasIndex && summary.TornBytes == 0);
        Assert.Equal(track.FramesWritten, summary.Samples);
        Assert.Null(track.EndReason);
        Assert.Equal(StageStates.Done, manifest.Stages.Single(s => s.Stage == StageNames.Video).State);
        AssertNotBlack(Path.Combine(folder, track.File), track.Width, track.Height);
    }

    [Trait("Category", "Hardware")]
    [ObsVirtualCameraFact]
    public async Task WhenObsStopsSendingTheCameraTrackEndsCleanly()
    {
        var camera = ObsVirtualCamera.Find()!;
        using var device = VideoDevice.Create();
        var index = 0u;
        var stopped = false;

        // The real graph and frames; the queue header the watch reads says "stopping" once the test flips it.
        var watch = new VirtualCameraWatch("OBS", () => stopped ? (index, 3u) : (++index, VirtualCameraWatch.StateReady), () => Environment.TickCount64);
        var source = new DirectShowCameraSource(device, camera.Id, camera.Name, () => DirectShowGraph.Open(camera.Id.Key, camera.Name), watch);
        var options = new VideoSessionOptions("camera", "camera", _folder, new VideoEncoderSettings(1280, 720), QpcClock.Now);
        await using var session = new VideoCaptureSession(options, source, new Mp4TrackWriterFactory(device));
        var problem = new TaskCompletionSource<VideoSessionEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Problem += (_, e) => problem.TrySetResult(e);
        await session.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromSeconds(4));

        stopped = true;
        var e = await problem.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var track = await session.StopAsync(CancellationToken.None);
        var summary = Mp4FileSummary.Read(Path.Combine(_folder, track.File));
        output.WriteLine($"{e.Error.Message} | {track.FramesWritten} frames, {summary.DurationSeconds:0.00} s, index {summary.HasIndex}");

        Assert.Equal(VideoSessionState.Ended, e.State);
        Assert.StartsWith("OBS Virtual Camera stopped at 0:0", e.Error.Message, StringComparison.Ordinal);
        Assert.Equal("sourceLost", track.EndReason);
        Assert.True(summary.IsFragmented && summary.HasIndex && summary.TornBytes == 0);
        Assert.InRange(summary.DurationSeconds, 3.0, 5.5);
    }

    /// <summary>The middle picture of the file is not black: OBS's picture went through conversion and encoding.</summary>
    private static void AssertNotBlack(string file, int width, int height)
    {
        using var reader = new Nv12SegmentReader([new ComposeSegment(file, 0)]);
        var frame = new Nv12Frame(width, height);
        var read = 0;
        while (read < 300 && reader.TryRead(frame, out _, out _))
        {
            read++;
        }

        var luma = frame.Data.AsSpan(0, width * height);
        var sum = 0L;
        foreach (var y in luma)
        {
            sum += y;
        }

        Assert.True(sum / luma.Length > 24, $"the picture is black (mean luma {sum / luma.Length})");
    }
}
