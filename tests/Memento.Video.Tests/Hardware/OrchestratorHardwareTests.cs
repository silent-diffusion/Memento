using System.Diagnostics;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Processing;
using Memento.Core.Projects;
using Memento.Core.Recording.Simulation;
using Memento.Core.Video;
using Memento.Video.Export;
using Memento.Video.Recovery;
using Memento.Video.Sources;
using Memento.Video.Tests.Export;
using Xunit.Abstractions;

namespace Memento.Video.Tests.Hardware;

/// <summary>
/// The desktop recorded through the real coordinator beside simulated audio, then checked by the video stage and
/// exported. The capture shows this PC's display, so everything is written to a temporary folder and deleted at the end.
/// </summary>
[Collection("Video hardware")]
public sealed class OrchestratorHardwareTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "memento-video-orch-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        // Finalized tracks are read-only; the capture shows this PC's display, so it must not be left behind.
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
    [VideoHardwareFact]
    public async Task TwentySecondsOfTheDesktopRecordBesideTheAudioAndExportAsOneFile()
    {
        var display = VideoSourceEnumerator.ListDisplays()[0];
        using var host = new OrchestratorHost(_folder);
        await host.Get<Core.Settings.ISettingsStore>().LoadAsync(CancellationToken.None);
        await host.Get<Core.Settings.ISettingsStore>().UpdateAsync(s => s with { Recording = s.Recording with { CheckpointSeconds = 5 } }, CancellationToken.None);
        await host.Get<Core.Library.LibraryOpener>().OpenAsync(CancellationToken.None);

        var clock = Stopwatch.StartNew();
        var started = await host.Recordings.StartAsync(
            new RecordingStartParams { Title = "Hardware check", Type = "meeting", SourceIds = [SimulatedAudioSourceProvider.Microphone.Id, display.Id.ToString()] },
            CancellationToken.None);
        var startMs = clock.ElapsedMilliseconds;
        await Task.Delay(TimeSpan.FromSeconds(20));
        var during = host.Recordings.Current!.VideoTracks;
        await host.Recordings.StopAsync(started.SessionId, CancellationToken.None);
        await host.Recordings.WhenIdleAsync();
        await host.Processing.WhenIdleAsync();

        var manifest = await host.Store.LoadAsync(started.RecordingId, CancellationToken.None);
        var folder = host.Store.GetProjectFolder(started.RecordingId);
        var track = Assert.Single(manifest.VideoTracks);
        var audio = Assert.Single(manifest.Tracks);
        var summary = Mp4FileSummary.Read(Path.Combine(folder, track.File));
        output.WriteLine($"start returned after {startMs} ms; video {track.Width}x{track.Height}@{track.FrameRate} {track.Encoder} (hardware {track.HardwareEncoder})");
        output.WriteLine($"video starts at {track.StartOffsetMs} ms, {track.DurationMs} ms long, {track.FramesWritten} frames, {track.FramesDropped} dropped, {new FileInfo(Path.Combine(folder, track.File)).Length} bytes");
        output.WriteLine($"audio {audio.DurationMs} ms; recording {manifest.DurationMs} ms; file {summary.DurationSeconds:0.000} s, {summary.Fragments.Count} fragments, index {summary.HasIndex}");

        Assert.Equal("screen", Assert.Single(during).TrackId);
        Assert.Equal(ProjectStates.Ready, manifest.State);
        Assert.True(manifest.HasVideo);
        Assert.InRange(track.StartOffsetMs, 0, 3000);
        Assert.InRange(track.DurationMs, 17_000, 21_000);
        Assert.InRange(track.StartOffsetMs + track.DurationMs, manifest.DurationMs - 1500, manifest.DurationMs + 1500);
        Assert.True(track.FramesDropped <= track.FramesWritten / 10, $"{track.FramesDropped} of {track.FramesWritten} frames dropped");
        Assert.True(summary.IsFragmented && summary.HasIndex && summary.TornBytes == 0);
        Assert.Equal(track.FramesWritten, summary.Samples);
        Assert.NotNull(track.Sha256);
        Assert.Equal(StageStates.Done, manifest.Stages.Single(s => s.Stage == StageNames.Video).State);

        // The export's "One file": the screen with the mix as AAC, encoded again; and the original frames copied.
        var mix = Path.Combine(folder, manifest.Mix!.File);
        var input = new VideoComposeInput(Path.Combine(folder, track.File), track.StartOffsetMs, track.DurationMs, track.Width, track.Height, track.FrameRate, track.BitrateBitsPerSecond);
        var encodeClock = Stopwatch.StartNew();
        var encoded = await Mp4Composer.ComposeAsync(new VideoComposeRequest(Path.Combine(_folder, "one-file.mp4"), input, null, mix, manifest.DurationMs, Reencode: true, []), null, CancellationToken.None);
        var encodeMs = encodeClock.ElapsedMilliseconds;
        var copyClock = Stopwatch.StartNew();
        var copied = await Mp4Composer.ComposeAsync(new VideoComposeRequest(Path.Combine(_folder, "original.mp4"), input, null, mix, manifest.DurationMs, Reencode: false, []), null, CancellationToken.None);
        output.WriteLine($"MP4 export: {encoded.Frames} frames in {encodeMs} ms ({encoded.Frames * 1000.0 / Math.Max(1, encodeMs):0} frames/s, hardware {encoded.Hardware}), {encoded.Bytes} bytes");
        output.WriteLine($"Original container: {copied.Frames} frames in {copyClock.ElapsedMilliseconds} ms, {copied.Bytes} bytes");

        Assert.InRange(encoded.Frames, (manifest.DurationMs * track.FrameRate / 1000) - 2, (manifest.DurationMs * track.FrameRate / 1000) + 2);
        Assert.Equal(track.FramesWritten, copied.Frames);
        Assert.InRange(Mp4ComposerTests.AudioSeconds(Path.Combine(_folder, "one-file.mp4")), (manifest.DurationMs / 1000.0) - 0.2, (manifest.DurationMs / 1000.0) + 0.2);
        Assert.InRange(Mp4ComposerTests.AudioSeconds(Path.Combine(_folder, "original.mp4")), (manifest.DurationMs / 1000.0) - 0.2, (manifest.DurationMs / 1000.0) + 0.2);
    }

    [Trait("Category", "Hardware")]
    [VideoHardwareFact]
    public async Task ARecordedScreenGetsASyntheticCameraInsetBurntIn()
    {
        var display = VideoSourceEnumerator.ListDisplays()[0];
        using var host = new OrchestratorHost(_folder);
        await host.Get<Core.Settings.ISettingsStore>().LoadAsync(CancellationToken.None);
        await host.Get<Core.Library.LibraryOpener>().OpenAsync(CancellationToken.None);
        var started = await host.Recordings.StartAsync(
            new RecordingStartParams { Title = "Inset check", Type = "meeting", SourceIds = [SimulatedAudioSourceProvider.Microphone.Id, display.Id.ToString()] },
            CancellationToken.None);
        await Task.Delay(TimeSpan.FromSeconds(6));
        await host.Recordings.StopAsync(started.SessionId, CancellationToken.None);
        await host.Recordings.WhenIdleAsync();
        await host.Processing.WhenIdleAsync();
        var manifest = await host.Store.LoadAsync(started.RecordingId, CancellationToken.None);
        var folder = host.Store.GetProjectFolder(started.RecordingId);
        var track = Assert.Single(manifest.VideoTracks);

        // No camera on this PC: a synthetic 720p camera that is a flat grey 60 for 4 s from the recording's start.
        var camera = SyntheticMedia.Video(Path.Combine(_folder, "camera.mp4"), 1280, 720, 30, 30 * 4, _ => 60);
        var request = new VideoComposeRequest(
            Path.Combine(_folder, "inset.mp4"),
            new VideoComposeInput(Path.Combine(folder, track.File), track.StartOffsetMs, track.DurationMs, track.Width, track.Height, track.FrameRate, track.BitrateBitsPerSecond),
            new VideoComposeInput(camera, 0, 4000, 1280, 720, 30, 2_000_000),
            Path.Combine(folder, manifest.Mix!.File),
            manifest.DurationMs,
            Reencode: true,
            [new VideoPipChange(0, true, VideoPipChange.TopLeft)]);

        var clock = Stopwatch.StartNew();
        var result = await Mp4Composer.ComposeAsync(request, null, CancellationToken.None);
        output.WriteLine($"{track.Width}x{track.Height} with inset: {result.Frames} frames in {clock.ElapsedMilliseconds} ms (hardware {result.Hardware})");

        var (x, y, w, h) = Nv12Frame.InsetRectangle(track.Width, track.Height, 1280, 720, VideoPipChange.TopLeft);
        var inset = SyntheticMedia.Luma(request.OutputPath, track.Width, track.Height, (x + 16, y + 16, w - 32, h - 32));
        var withCamera = inset.Where(f => f.Time > (track.StartOffsetMs + 200) * TimeSpan.TicksPerMillisecond && f.Time < 3_900 * TimeSpan.TicksPerMillisecond).ToList();
        Assert.NotEmpty(withCamera);
        Assert.All(withCamera, f => Assert.InRange(f.Luma, 52, 68));

        // Before the screen's first frame the picture is black around the inset; after the camera ended, no inset.
        var corner = SyntheticMedia.Luma(request.OutputPath, track.Width, track.Height, (track.Width - 64, track.Height - 64, 32, 32));
        Assert.All(corner.Where(f => f.Time < (track.StartOffsetMs - 40) * TimeSpan.TicksPerMillisecond), f => Assert.InRange(f.Luma, 10, 24));
    }
}
