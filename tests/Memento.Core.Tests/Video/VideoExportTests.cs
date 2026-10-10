using System.Text.Json;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Export;
using Memento.Core.Tests.Fakes;
using Memento.Core.Tests.M3;
using Memento.Core.Video;
using Microsoft.Extensions.DependencyInjection;

namespace Memento.Core.Tests.Video;

/// <summary>The Export dialog's Video row (DESIGN.md §18.4) through <c>export.estimate</c> and <c>export.run</c>, composing with <see cref="FakeVideoFiles"/>.</summary>
public sealed class VideoExportTests : IDisposable
{
    private readonly FakeVideoCapture _capture = new();
    private readonly FakeVideoFiles _files = new();
    private readonly M3Host _m3;

    public VideoExportTests()
    {
        _m3 = new M3Host(configure: services =>
        {
            services.AddSingleton<IVideoCapture>(_capture);
            services.AddSingleton<IVideoFiles>(_files);
        });
    }

    private string Destination => _m3.Directory.File("Exports");

    public void Dispose() => _m3.Dispose();

    private static ExportSelection VideoOnly(string format, string layout) => new()
    {
        Video = new ExportVideoChoice { On = true, Format = format, Layout = layout },
    };

    private async Task<string> RecordAsync(params string[] video)
    {
        var id = await _m3.RecordAsync("Demo with video", 3, [TestRecordings.Mic, .. video]);
        await _m3.Host.Processing.WhenIdleAsync();
        return id;
    }

    private async Task<(List<string> Names, JsonElement Estimate)> EstimateAsync(string id, ExportSelection selection)
    {
        var estimate = await _m3.ResultAsync("export.estimate", new { recordingId = id, selection });
        return (estimate.GetProperty("items").EnumerateArray().Where(i => i.GetProperty("component").GetString() == "video").Select(i => i.GetProperty("name").GetString()!).ToList(), estimate);
    }

    private async Task<string> BaseNameAsync(string id)
    {
        var manifest = await _m3.Host.Store.LoadAsync(id, CancellationToken.None);
        return ExportNaming.BaseName(manifest.Details.Title, manifest.CreatedAt);
    }

    [Fact]
    public async Task TheEstimateNamesOneFileOrOneFilePerTrack()
    {
        var id = await RecordAsync(FakeVideoCapture.Display1, FakeVideoCapture.Camera);
        var baseName = await BaseNameAsync(id);

        Assert.Equal([baseName + ".mp4"], (await EstimateAsync(id, VideoOnly("mp4", "oneFile"))).Names);
        Assert.Equal([baseName + " - screen.mp4", baseName + " - camera.mp4"], (await EstimateAsync(id, VideoOnly("mp4", "twoFiles"))).Names);

        // The original frames cannot have the inset burnt in: the camera comes as its own file.
        Assert.Equal([baseName + ".mp4", baseName + " - camera.mp4"], (await EstimateAsync(id, VideoOnly("original", "oneFile"))).Names);

        // Sizes: the recorded size plus the audio at 192 kb/s (3 s here).
        var (_, estimate) = await EstimateAsync(id, VideoOnly("original", "twoFiles"));
        var screen = estimate.GetProperty("items").EnumerateArray().First(i => i.GetProperty("name").GetString()!.EndsWith(" - screen.mp4", StringComparison.Ordinal));
        Assert.InRange(screen.GetProperty("bytes").GetInt64(), 4096 + (3000 * 192 / 8) - 100, 4096 + (10_040 * 192 / 8));
    }

    [Fact]
    public async Task UnavailableVideoSaysWhy()
    {
        var audioOnly = await RecordAsync();
        var (names, estimate) = await EstimateAsync(audioOnly, VideoOnly("mp4", "oneFile"));
        Assert.Empty(names);
        Assert.Equal("""[{"component":"video","reason":"No video was recorded"}]""", estimate.GetProperty("unavailable").GetRawText());

        var withVideo = await RecordAsync(FakeVideoCapture.Display1);
        await _m3.ResultAsync("video.remove", new { recordingId = withVideo });
        (_, estimate) = await EstimateAsync(withVideo, VideoOnly("mp4", "oneFile"));
        Assert.Equal("""[{"component":"video","reason":"The video was removed"}]""", estimate.GetProperty("unavailable").GetRawText());

        var bad = await _m3.CallAsync("export.estimate", new { recordingId = withVideo, selection = VideoOnly("mkv", "oneFile") });
        Assert.Equal("bridge.invalidParams", bad.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task OneFileComposesTheScreenWithTheInsetAndTheMixIntoTheManifest()
    {
        var id = await RecordAsync(FakeVideoCapture.Display1, FakeVideoCapture.Camera);
        var baseName = await BaseNameAsync(id);

        var jobId = (await _m3.ResultAsync("export.run", new { recordingId = id, selection = VideoOnly("mp4", "oneFile"), destination = new { folder = Destination, createSubfolder = false }, remember = false })).GetProperty("jobId").GetString()!;
        await _m3.Get<ExportService>().WhenIdleAsync();
        var done = await _m3.Sink.WaitForAsync(BridgeEventNames.ExportProgress, p => p.GetProperty("jobId").GetString() == jobId && p.GetProperty("state").GetString() != "running");

        Assert.Equal("done", done.GetProperty("state").GetString());
        var request = Assert.Single(_files.Composed);
        var folder = _m3.Host.Store.GetProjectFolder(id);
        Assert.True(request.Reencode);
        Assert.Equal(Path.Combine(folder, "tracks", "screen.mp4"), request.Video.Path);
        Assert.Equal(Path.Combine(folder, "tracks", "camera.mp4"), request.Inset!.Path);
        Assert.Equal(Path.Combine(folder, "mix.flac"), request.AudioPath);
        Assert.Equal(40, request.Video.StartOffsetMs);
        Assert.Equal(10_040, request.DurationMs);
        Assert.Equal("bottomRight", Assert.Single(request.Pip).Corner);
        Assert.True(File.Exists(Path.Combine(Destination, baseName + ".mp4")));
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Destination, "manifest.json")));
        Assert.Contains(manifest.RootElement.GetProperty("files").EnumerateArray(), f => f.GetProperty("name").GetString() == baseName + ".mp4" && f.GetProperty("bytes").GetInt64() == 2048);
    }
}
