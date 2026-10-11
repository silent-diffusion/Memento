using System.Text.Json;
using Memento.Core.Settings;
using Memento.Core.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Memento.Core.Tests.Settings;

/// <summary>
/// A settings.json written by 1.3.0 (schema 1) with every field it knew set away from its default, opened by 2.0: every
/// value is kept except the two that did nothing in 1.x (keep only the mix, the live transcript), the 2.0 fields read
/// their defaults, and the first save writes schema 2 with all of it still there.
/// </summary>
public sealed class SettingsV1MigrationTests : IDisposable
{
    private const string LibraryPath = @"D:\Memento Library";
    private const string ExportFolder = @"D:\Exports";

    // Every member of the 1.3.0 AppSettings tree (git show v1.3.0:src/Memento.Core/Settings), none at its default.
    private static readonly string Version1File = $$"""
        {
          "schemaVersion": 1,
          "theme": "dark",
          "libraryPath": {{JsonSerializer.Serialize(LibraryPath)}},
          "listDensity": "compact",
          "recording": {
            "defaultType": "interview",
            "defaultSourceIds": ["mic:{0.0.1.00000000}.{a}", "loopback:default"],
            "keepSeparateTracks": false,
            "storage": { "codec": "mp3", "bitrateKbps": 128, "downmixMono": true, "keepOnlyMix": true },
            "checkpointSeconds": 60,
            "lowSpaceGb": 25
          },
          "transcription": {
            "auto": false,
            "timing": "during",
            "pauseWhenBusy": false,
            "modelId": "whisper-medium",
            "cpuFallbackModelId": "whisper-base",
            "language": "en",
            "keepWordTimestamps": false,
            "lowConfidenceThreshold": 0.35
          },
          "speakers": { "identify": false, "expectedSpeakers": 3, "rememberRenamed": true, "embeddingModelId": "nemo-titanet-small" },
          "history": { "keepVersions": false, "keepDays": 30 },
          "general": { "startWithWindows": true, "keepRunningInTray": true, "language": "en", "autoUpdate": false },
          "export": {
            "saveCopiesOutside": true,
            "defaultFolder": {{JsonSerializer.Serialize(ExportFolder)}},
            "askWhereEachTime": false,
            "createSubfolder": false,
            "defaults": {
              "audioMixed": { "on": false, "format": "wav", "bitrateKbps": null },
              "tracks": { "on": true, "format": "mp3", "bitrateKbps": 192 },
              "transcript": { "on": true, "formats": ["srt", "txt"], "options": { "timestamps": false, "speakers": false, "layout": "lines" } },
              "documents": { "on": true, "documentIds": [], "format": "pdf" },
              "details": { "on": true },
              "attachments": { "on": true }
            }
          },
          "ai": {
            "enabled": true,
            "askBeforeSend": false,
            "keepRecord": false,
            "share": { "transcript": false, "details": false, "participants": false, "agenda": false, "highlights": false, "attachments": true },
            "defaultProviderId": "anthropic",
            "localModelId": "ministral-3-3b",
            "providers": { "anthropic": { "model": "claude-sonnet-4-5" }, "openai": { "model": "gpt-5-mini" } }
          },
          "storage": { "reclaimOlderThanDays": 180 },
          "documents": { "defaultTemplateId": "builtin.minutes", "defaultStyleId": "builtin.classic" }
        }
        """;

    private readonly TempDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    private string SettingsPath => _directory.File("settings.json");

    [Fact]
    public async Task A130SettingsFileKeepsEveryValueExceptTheTwoThatDidNothingIn1x()
    {
        await File.WriteAllTextAsync(SettingsPath, Version1File);
        using var store = new JsonSettingsStore(SettingsPath, NullLogger<JsonSettingsStore>.Instance);

        AssertCarriedOver(await store.LoadAsync(CancellationToken.None), rememberVoices: false);
    }

    [Fact]
    public async Task TheFirstSaveWritesSchema2AndKeepsEveryValue()
    {
        await File.WriteAllTextAsync(SettingsPath, Version1File);
        using (var store = new JsonSettingsStore(SettingsPath, NullLogger<JsonSettingsStore>.Instance))
        {
            await store.LoadAsync(CancellationToken.None);
            await store.UpdateAsync(s => s with { Speakers = s.Speakers with { RememberVoices = true } }, CancellationToken.None);
        }

        using (var document = JsonDocument.Parse(await File.ReadAllTextAsync(SettingsPath)))
        {
            Assert.Equal(2, document.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.True(document.RootElement.GetProperty("speakers").GetProperty("rememberRenamed").GetBoolean());
        }

        using var reopened = new JsonSettingsStore(SettingsPath, NullLogger<JsonSettingsStore>.Instance);
        var settings = await reopened.LoadAsync(CancellationToken.None);
        AssertCarriedOver(settings, rememberVoices: true);
    }

    private static void AssertCarriedOver(AppSettings s, bool rememberVoices)
    {
        Assert.Equal(ThemePreference.Dark, s.Theme);
        Assert.Equal(LibraryPath, s.LibraryPath);
        Assert.Equal(ListDensity.Compact, s.ListDensity);

        Assert.Equal("interview", s.Recording.DefaultType);
        Assert.Equal(["mic:{0.0.1.00000000}.{a}", "loopback:default"], s.Recording.DefaultSourceIds);
        Assert.True(s.Recording.KeepSeparateTracks); // Fixed since M1: every source is its own track.
        Assert.Equal((StorageSettings.Mp3, 128, true), (s.Recording.Storage.Codec, s.Recording.Storage.BitrateKbps, s.Recording.Storage.DownmixMono));
        Assert.Equal((60, 25), (s.Recording.CheckpointSeconds, s.Recording.LowSpaceGb));
        // Stored by 1.x before they did anything: they start off in 2.0.
        Assert.False(s.Recording.Storage.KeepOnlyMix);
        Assert.Equal(TranscriptionSettings.TimingAfter, s.Transcription.Timing);
        // 2.0 fields read their defaults.
        Assert.False(s.Recording.CaptureVideo);
        Assert.Equal(new VideoSettings(), s.Recording.Video);
        Assert.False(s.Transcription.LiveOnGpu);
        Assert.Equal(rememberVoices, s.Speakers.RememberVoices);
        Assert.Null(s.Storage.RemoveVideoOlderThanDays);
        Assert.Null(s.Documents.OcrEngine);

        var t = s.Transcription;
        Assert.False(t.Auto);
        Assert.False(t.PauseWhenBusy);
        Assert.Equal(("whisper-medium", "whisper-base", "en"), (t.ModelId, t.CpuFallbackModelId, t.Language));
        Assert.False(t.KeepWordTimestamps);
        Assert.Equal(0.35, t.LowConfidenceThreshold);

        Assert.False(s.Speakers.Identify);
        Assert.Equal(3, s.Speakers.ExpectedSpeakers);
        Assert.True(s.Speakers.RememberRenamed);
        Assert.Equal("nemo-titanet-small", s.Speakers.EmbeddingModelId);
        Assert.Equal((false, 30), (s.History.KeepVersions, s.History.KeepDays));

        Assert.Equal((true, true, "en", false), (s.General.StartWithWindows, s.General.KeepRunningInTray, s.General.Language, s.General.AutoUpdate));

        var e = s.Export;
        Assert.Equal((true, ExportFolder, false, false), (e.SaveCopiesOutside, e.DefaultFolder, e.AskWhereEachTime, e.CreateSubfolder));
        Assert.Equal((false, "wav"), (e.Defaults.AudioMixed.On, e.Defaults.AudioMixed.Format));
        Assert.Equal((true, "mp3", 192), (e.Defaults.Tracks.On, e.Defaults.Tracks.Format, e.Defaults.Tracks.BitrateKbps));
        Assert.Equal(["srt", "txt"], e.Defaults.Transcript.Formats);
        Assert.Equal((false, false, "lines"), (e.Defaults.Transcript.Options!.Timestamps, e.Defaults.Transcript.Options.Speakers, e.Defaults.Transcript.Options.Layout));
        Assert.Equal((true, "pdf"), (e.Defaults.Documents.On, e.Defaults.Documents.Format));
        Assert.True(e.Defaults.Details.On && e.Defaults.Attachments.On);
        // No video row in a 1.x file: export's video choice is off.
        Assert.True(e.Defaults.Video is null || !e.Defaults.Video.On);

        var ai = s.Ai;
        Assert.Equal((true, false, false), (ai.Enabled, ai.AskBeforeSend, ai.KeepRecord));
        Assert.Equal((false, false, false, false, false, true), (ai.Share.Transcript, ai.Share.Details, ai.Share.Participants, ai.Share.Agenda, ai.Share.Highlights, ai.Share.Attachments));
        Assert.Equal(("anthropic", "ministral-3-3b"), (ai.DefaultProviderId, ai.LocalModelId));
        Assert.Equal(("claude-sonnet-4-5", "gpt-5-mini"), (ai.Providers.Anthropic.Model, ai.Providers.Openai.Model));

        Assert.Equal(180, s.Storage.ReclaimOlderThanDays);
        Assert.Equal(("builtin.minutes", "builtin.classic"), (s.Documents.DefaultTemplateId, s.Documents.DefaultStyleId));
    }
}
