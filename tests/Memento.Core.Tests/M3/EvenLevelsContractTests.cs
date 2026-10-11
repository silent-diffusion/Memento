using System.Text.Json;
using Memento.Core.Audio;
using Memento.Core.Library;
using Memento.Core.Projects;
using Memento.Core.Settings;
using Memento.Core.Tests.Audio;
using Memento.Core.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using static Memento.Core.Tests.Fakes.TestRecordings;

namespace Memento.Core.Tests.M3;

/// <summary>
/// "Even out source levels" (after 2.0.0-beta.1, ENGINE-NOTES.md §Q): the setting, the gains recorded in the manifest
/// when the mix is built, <c>project.get</c>'s <c>mixLevels</c>, and <c>project.rebuildMix</c> with the <c>mix</c> stage,
/// all strictly as BRIDGE.md "Even out source levels" says.
/// </summary>
public sealed class EvenLevelsContractTests : IDisposable
{
    private readonly M3Host _m3 = new();

    public void Dispose() => _m3.Dispose();

    private Task<AppSettings> EvenLevelsAsync(bool on) =>
        _m3.Host.Settings.UpdateAsync(s => s with { Recording = s.Recording with { Storage = s.Recording.Storage with { EvenLevels = on } } }, CancellationToken.None);

    private async Task<string> StoredAsync(bool evenLevels)
    {
        await EvenLevelsAsync(evenLevels);
        var id = await _m3.RecordAsync("Levels", 2, Mic, SystemAudio);
        await _m3.Host.Processing.WhenIdleAsync().WaitAsync(Patience.Ceiling);
        return id;
    }

    /// <summary>
    /// Replaces the stored tracks with loud system audio (-15 dBFS speech with digital silence between) and a quiet
    /// microphone (-46 dBFS speech over -66 dBFS noise), as a far-away microphone records beside a video playing.
    /// </summary>
    private async Task MakeTheMicrophoneQuietAsync(string id)
    {
        var manifest = await _m3.Host.Store.LoadAsync(id, CancellationToken.None);
        var folder = _m3.Host.Store.GetProjectFolder(id);
        var random = new Random(5);
        foreach (var track in manifest.Tracks)
        {
            var path = Path.Combine(folder, track.File);
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
            if (track.Id == "mic")
            {
                WavTestFiles.Write(path, PcmFormat.Pcm16(48_000, 1), 96_000, (f, _) =>
                    (float)(((f / 24_000 % 2 == 1) ? 0.007 * Math.Sin(2 * Math.PI * 180 * f / 48_000) : 0) + (0.0009 * ((random.NextDouble() * 2) - 1))));
            }
            else
            {
                WavTestFiles.Write(path, PcmFormat.Pcm16(48_000, 2), 96_000, (f, _) =>
                    f / 24_000 % 2 == 0 ? (float)(0.25 * Math.Sin(2 * Math.PI * 300 * f / 48_000)) : 0f);
            }
        }
    }

    [Fact]
    public async Task TheSettingIsOnByDefaultAlsoForASettingsFileFromAnEarlierBuild()
    {
        Assert.True(new StorageSettings().EvenLevels);
        var settings = await _m3.ResultAsync("settings.get", new { });
        Assert.True(settings.GetProperty("recording").GetProperty("storage").GetProperty("evenLevels").GetBoolean());

        var path = _m3.Directory.File("old-settings.json");
        await File.WriteAllTextAsync(path, """{ "schemaVersion": 2, "recording": { "storage": { "codec": "flac", "keepOnlyMix": false } } }""");
        var old = new JsonSettingsStore(path, NullLogger<JsonSettingsStore>.Instance);
        Assert.True((await old.LoadAsync(CancellationToken.None)).Recording.Storage.EvenLevels);
    }

    [Fact]
    public async Task SettingsSetTurnsItOffAndAnOmittedFieldTakesTheDefault()
    {
        var current = await _m3.ResultAsync("settings.get", new { });
        var recording = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(current.GetProperty("recording").GetRawText())!;
        recording["storage"] = JsonSerializer.SerializeToElement(new { codec = "flac", bitrateKbps = (int?)null, downmixMono = false, keepOnlyMix = false, evenLevels = false });
        await _m3.ResultAsync("settings.set", new { recording });
        Assert.False(_m3.Host.Settings.Current.Recording.Storage.EvenLevels);

        recording["storage"] = JsonSerializer.SerializeToElement(new { codec = "flac" });
        await _m3.ResultAsync("settings.set", new { recording });
        Assert.True(_m3.Host.Settings.Current.Recording.Storage.EvenLevels);
    }

    [Fact]
    public async Task TheGainsGoIntoTheManifestHistoryAndProjectGet()
    {
        var id = await StoredAsync(evenLevels: true);

        var manifest = await _m3.Host.Store.LoadAsync(id, CancellationToken.None);
        var levels = Assert.IsType<ProjectMixLevels>(manifest.Mix!.EvenLevels);
        Assert.Equal(["mic", "system"], levels.Tracks.Select(t => t.TrackId).Order(StringComparer.Ordinal));
        Assert.All(levels.Tracks, t => Assert.InRange(t.GainDb, 0, MixLevels.MaxLiftDb));
        var stored = Assert.Single(await _m3.Host.Store.ReadHistoryAsync(id, CancellationToken.None), h => h.Stage == StageNames.Stored && h.Event == "completed");
        Assert.Contains("levels evened: ", stored.Detail, StringComparison.Ordinal);

        var project = await _m3.ResultAsync("project.get", new { recordingId = id });
        var mixLevels = project.GetProperty("mixLevels");
        Assert.Equal("at,tracks", string.Join(",", mixLevels.EnumerateObject().Select(p => p.Name)));
        Assert.All(mixLevels.GetProperty("tracks").EnumerateArray(), t => Assert.Equal("trackId,levelDbfs,gainDb", string.Join(",", t.EnumerateObject().Select(p => p.Name))));
        Assert.DoesNotContain("?v=", project.GetProperty("mixUrl").GetString(), StringComparison.Ordinal);

        // The file on disk carries the same, under mix.evenLevels.
        using var file = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_m3.Host.Store.GetProjectFolder(id), "project.json")));
        Assert.Equal(JsonValueKind.Array, file.RootElement.GetProperty("mix").GetProperty("evenLevels").GetProperty("tracks").ValueKind);
    }

    [Fact]
    public async Task WithTheSettingOffTheMixSumsTheTracksAsRecorded()
    {
        var id = await StoredAsync(evenLevels: false);

        Assert.Null((await _m3.Host.Store.LoadAsync(id, CancellationToken.None)).Mix!.EvenLevels);
        Assert.Equal(JsonValueKind.Null, (await _m3.ResultAsync("project.get", new { recordingId = id })).GetProperty("mixLevels").ValueKind);
    }

    [Fact]
    public async Task RebuildingTheMixWithEvenedLevelsAndBackAsRecorded()
    {
        var id = await StoredAsync(evenLevels: false);
        await MakeTheMicrophoneQuietAsync(id);
        var folder = _m3.Host.Store.GetProjectFolder(id);
        var before = await _m3.Host.Store.LoadAsync(id, CancellationToken.None);
        var trackHashes = before.Tracks.Select(t => t.Sha256).ToList();

        var answer = await _m3.ResultAsync("project.rebuildMix", new { recordingId = id, evenLevels = true });
        Assert.Equal(JsonValueKind.Object, answer.ValueKind);
        Assert.Empty(answer.EnumerateObject());
        await _m3.Host.Processing.WhenIdleAsync().WaitAsync(Patience.Ceiling);

        var evened = await _m3.Host.Store.LoadAsync(id, CancellationToken.None);
        Assert.Null(evened.MixRebuild);
        Assert.Equal(StageStates.Done, Assert.Single(evened.Stages, s => s.Stage == StageNames.Mix).State);
        Assert.Equal("mix.flac", evened.Mix!.File);
        Assert.NotEqual(before.Mix!.Sha256, evened.Mix.Sha256);
        Assert.Equal(await FileHashes.Sha256Async(Path.Combine(folder, "mix.flac"), CancellationToken.None), evened.Mix.Sha256);
        Assert.Equal(evened.Mix.Sha256, evened.Integrity.Files["mix.flac"]);
        Assert.Equal(trackHashes, evened.Tracks.Select(t => t.Sha256));
        var mic = Assert.Single(evened.Mix.EvenLevels!.Tracks, t => t.TrackId == "mic");
        Assert.InRange(mic.GainDb, 10, MixLevels.MaxLiftDb);
        Assert.Empty(Directory.GetFiles(folder, "mix.rebuilt*"));
        Assert.True(File.GetAttributes(Path.Combine(folder, "mix.flac")).HasFlag(FileAttributes.ReadOnly));

        var history = await _m3.Host.Store.ReadHistoryAsync(id, CancellationToken.None);
        Assert.Contains(history, h => h.Stage == StageNames.Mix && h.Event == "started" && h.Summary == "Rebuilding the mix with evened levels");
        var done = Assert.Single(history, h => h.Stage == StageNames.Mix && h.Event == "completed");
        Assert.Equal("Rebuilt the mix with evened levels", done.Summary);
        Assert.Contains("Simulated microphone lifted ", done.Detail, StringComparison.Ordinal);
        Assert.Contains("To undo it, choose Rebuild the mix as recorded in Review › More › Reprocess.", done.Detail, StringComparison.Ordinal);

        var project = await _m3.ResultAsync("project.get", new { recordingId = id });
        Assert.EndsWith($"/mix.flac?v={evened.Mix.Sha256[..8]}", project.GetProperty("mixUrl").GetString(), StringComparison.Ordinal);
        Assert.EndsWith($"/peaks.json?v={evened.Mix.Sha256[..8]}", project.GetProperty("peaksUrl").GetString(), StringComparison.Ordinal);
        Assert.Equal(JsonValueKind.Object, project.GetProperty("mixLevels").ValueKind);

        await _m3.ResultAsync("project.rebuildMix", new { recordingId = id, evenLevels = false });
        await _m3.Host.Processing.WhenIdleAsync().WaitAsync(Patience.Ceiling);

        var back = await _m3.Host.Store.LoadAsync(id, CancellationToken.None);
        Assert.Null(back.Mix!.EvenLevels);
        Assert.NotEqual(evened.Mix.Sha256, back.Mix.Sha256);
        Assert.Equal(back.Mix.Sha256, back.Integrity.Files["mix.flac"]);
        Assert.Contains(await _m3.Host.Store.ReadHistoryAsync(id, CancellationToken.None), h => h.Stage == StageNames.Mix && h.Summary == "Rebuilt the mix as recorded");
    }

    [Fact]
    public async Task RebuildMixRefusesStrictly()
    {
        var id = await StoredAsync(evenLevels: false);

        Assert.Equal("project.notFound", (await _m3.ErrorAsync("project.rebuildMix", new { recordingId = "20260101-000000-aaaaaa", evenLevels = true })).GetProperty("code").GetString());
        Assert.Equal("bridge.invalidParams", (await _m3.ErrorAsync("project.rebuildMix", new { recordingId = id })).GetProperty("code").GetString());
        Assert.Equal("bridge.invalidParams", (await _m3.ErrorAsync("project.rebuildMix", new { recordingId = id, evenLevels = true, gainDb = 6 })).GetProperty("code").GetString());

        var folder = _m3.Host.Store.GetProjectFolder(id);
        var mic = (await _m3.Host.Store.LoadAsync(id, CancellationToken.None)).Tracks.Single(t => t.Id == "mic");
        File.SetAttributes(Path.Combine(folder, mic.File), FileAttributes.Normal);
        File.Move(Path.Combine(folder, mic.File), Path.Combine(folder, "moved.flac"));
        var missing = await _m3.ErrorAsync("project.rebuildMix", new { recordingId = id, evenLevels = true });
        Assert.Equal("project.mixUnavailable", missing.GetProperty("code").GetString());
        Assert.Contains("Nothing was changed", missing.GetProperty("message").GetString(), StringComparison.Ordinal);

        await _m3.Get<ProjectCatalog>().UpdateAsync(id, m => m with { MixOnly = new ProjectMixOnly(DateTimeOffset.Now, ["mic", "system"], 1) }, CancellationToken.None);
        var mixOnly = await _m3.ErrorAsync("project.rebuildMix", new { recordingId = id, evenLevels = true });
        Assert.Equal("project.mixUnavailable", mixOnly.GetProperty("code").GetString());
        Assert.Contains("keeps only its mix", mixOnly.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Null((await _m3.Host.Store.LoadAsync(id, CancellationToken.None)).MixRebuild);
    }
}
