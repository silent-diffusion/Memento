using System.Text.Json;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Tests.Fakes;

namespace Memento.Core.Tests.Bridge;

/// <summary>
/// Pins the exact JSON of every method and event in docs/BRIDGE.md. If one of these fails, update
/// <c>ui/src/bridge/types.ts</c> in the same change.
/// </summary>
public sealed class ContractSerializationTests : IDisposable
{
    private static readonly DateTimeOffset At = new(2026, 10, 6, 10, 0, 0, TimeSpan.FromHours(1));

    private readonly BridgeTestHost _host = new();

    public void Dispose() => _host.Dispose();

    private async Task<string> CallAsync(string method, string parameters = "{}") =>
        await _host.Router.HandleAsync($$"""{"id":1,"method":"{{method}}","params":{{parameters}}}""", CancellationToken.None);

    private static string Names(JsonElement element) => string.Join(",", element.EnumerateObject().Select(p => p.Name));

    [Fact]
    public async Task AppVersion()
    {
        _host.Theme.IsDark = true;

        var json = await CallAsync("app.version");

        Assert.Equal("""{"id":1,"result":{"version":"0.1.0","osVersion":"Windows 10.0.26200","isDarkTheme":true}}""", json);
    }

    [Fact]
    public async Task SettingsGetReturnsDefaultsWithTheEffectiveLibraryPath()
    {
        var json = await CallAsync("settings.get");

        using var document = JsonDocument.Parse(json);
        var result = document.RootElement.GetProperty("result");
        Assert.Equal("theme,libraryPath,listDensity,recording,transcription,speakers,history,general,export,ai,storage,documents", Names(result));
        Assert.Equal(AppPaths.DefaultLibrary, result.GetProperty("libraryPath").GetString());
        Assert.Contains("\"theme\":\"system\"", json, StringComparison.Ordinal);
        Assert.Contains("\"listDensity\":\"comfortable\"", json, StringComparison.Ordinal);
        Assert.Equal(
            """{"defaultType":"meeting","defaultSourceIds":[],"keepSeparateTracks":true,"storage":{"codec":"flac","bitrateKbps":null,"downmixMono":false,"keepOnlyMix":false},"checkpointSeconds":30,"lowSpaceGb":10}""",
            result.GetProperty("recording").GetRawText());
    }

    [Fact]
    public async Task SettingsSetReturnsTheUpdatedSnapshot()
    {
        var json = await CallAsync("settings.set", """{"theme":"dark"}""");

        using var document = JsonDocument.Parse(json);
        var result = document.RootElement.GetProperty("result");
        Assert.Equal("theme,libraryPath,listDensity,recording,transcription,speakers,history,general,export,ai,storage,documents", Names(result));
        Assert.Equal("dark", result.GetProperty("theme").GetString());
    }

    [Fact]
    public async Task LibraryListIsEmptyWithTheDocumentedShape()
    {
        var json = await CallAsync("library.list");

        Assert.Equal("""{"id":1,"result":{"recordings":[],"totalDurationMs":0,"totalCount":0}}""", json);
    }

    [Fact]
    public async Task LibraryProcessingIsNullWhenNothingRuns()
    {
        var json = await CallAsync("library.processing");

        Assert.Equal("""{"id":1,"result":{"current":null,"othersCount":0}}""", json);
    }

    [Fact]
    public async Task OpenExternal()
    {
        var json = await CallAsync("app.openExternal", """{"url":"https://github.com/silent-diffusion/Memento"}""");

        Assert.Equal("""{"id":1,"result":{"opened":true}}""", json);
    }

    [Fact]
    public async Task UiReady()
    {
        var json = await CallAsync("ui.ready");

        Assert.Equal("""{"id":1,"result":{}}""", json);
    }

    [Fact]
    public async Task SourcesList()
    {
        var json = await CallAsync("sources.list");

        Assert.Equal(
            """{"id":1,"result":{"audio":[{"id":"mic:simulated-1","kind":"microphone","name":"Simulated microphone","detail":"Simulated · 48 kHz mono","isDefault":true,"processId":null},{"id":"system:simulated","kind":"system","name":"Simulated system audio","detail":"Everything this PC plays (simulated)","isDefault":true,"processId":null},{"id":"app:4242","kind":"application","name":"Simulated meeting app","detail":"Only this app (simulated)","isDefault":false,"processId":4242}],"videoAvailable":false}}""",
            json.Replace("\\u00B7", "·", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RecordingCurrentIsNullWithoutASession()
    {
        Assert.Equal("""{"id":1,"result":{"session":null}}""", await CallAsync("recording.current"));
    }

    [Fact]
    public async Task RecoveryListIsEmpty()
    {
        Assert.Equal("""{"id":1,"result":{"items":[]}}""", await CallAsync("recovery.list"));
    }

    [Fact]
    public async Task StatusGet()
    {
        _host.FreeSpace.FreeBytes = 1000;

        var json = await CallAsync("status.get");

        using var document = JsonDocument.Parse(json);
        var result = document.RootElement.GetProperty("result");
        Assert.Equal("engine,storage,recording,processingPaused,export,update", Names(result));
        Assert.Equal("""{"freeBytes":1000,"lowSpace":true}""", result.GetProperty("storage").GetRawText());
        Assert.Equal("""{"active":false,"lastCheckpointAt":null,"lostSource":null}""", result.GetProperty("recording").GetRawText());
        Assert.Equal("Low disk space", result.GetProperty("processingPaused").GetString());
        var engine = result.GetProperty("engine");
        Assert.Equal("ready,device,detail", Names(engine));
        Assert.False(engine.GetProperty("ready").GetBoolean());
        Assert.Equal(JsonValueKind.Null, engine.GetProperty("device").ValueKind);

        // No GPU and no model installed: the recommended CPU model, not ready.
        var detail = engine.GetProperty("detail");
        Assert.Equal("ready,device,gpuName,freeVramBytes,model,paused", Names(detail));
        Assert.Equal("whisper-small", detail.GetProperty("model").GetString());
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("gpuName").ValueKind);
    }

    [Fact]
    public async Task DialogPickFolder()
    {
        _host.FolderPicker.Answer = "D:\\Recordings";

        Assert.Equal("""{"id":1,"result":{"path":"D:\\Recordings"}}""", await CallAsync("dialog.pickFolder", """{"title":"Choose a library folder"}"""));
    }

    [Fact]
    public async Task ErrorResponse()
    {
        var json = await CallAsync("settings.set", """{"theme":"purple"}""");

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal(["id", "error"], root.EnumerateObject().Select(p => p.Name));
        Assert.Equal(1, root.GetProperty("id").GetInt64());
        var error = root.GetProperty("error");
        Assert.Equal(["code", "message", "detail"], error.EnumerateObject().Select(p => p.Name));
        Assert.Equal("settings.invalidValue", error.GetProperty("code").GetString());
        Assert.Equal("Theme 'purple' is not available. Choose system, light, dark.", error.GetProperty("message").GetString());
        Assert.Equal(JsonValueKind.Null, error.GetProperty("detail").ValueKind);
    }

    [Fact]
    public void RecordingSummaryShape()
    {
        var summary = new RecordingSummary(
            "20261006-100000-k3f9ab",
            "Q3 planning sync",
            "meeting",
            At,
            3_734_000,
            5,
            HasVideo: false,
            [new StageStatus("transcript", "active", 64, "64% · local GPU"), new StageStatus("speakers", "queued", null, "Queued")],
            ["Avery", "Rowan"],
            IsProcessing: true,
            "ready",
            SizeBytes: 123_456_789);

        var json = JsonSerializer.Serialize(
            new LibraryListResult([summary], summary.DurationMs, 1),
            BridgeJsonContext.Default.LibraryListResult);

        Assert.Equal(
            """{"recordings":[{"id":"20261006-100000-k3f9ab","title":"Q3 planning sync","type":"meeting","createdAt":"2026-10-06T10:00:00+01:00","durationMs":3734000,"participantCount":5,"hasVideo":false,"stages":[{"stage":"transcript","state":"active","percent":64,"label":"64% \u00B7 local GPU"},{"stage":"speakers","state":"queued","percent":null,"label":"Queued"}],"people":["Avery","Rowan"],"isProcessing":true,"state":"ready","sizeBytes":123456789,"matchSnippet":null}],"totalDurationMs":3734000,"totalCount":1}""",
            json);
    }

    [Fact]
    public void ProjectShape()
    {
        var summary = new RecordingSummary("20261006-100000-k3f9ab", "Sync", "meeting", At, 1000, 0, false, [], [], false, "ready", 4096);
        var project = new Project(
            summary,
            new RecordingDetails("Sync", "meeting", [], "", "", "", "", "", [], Agenda.Empty),
            [new Track("mic", "mic:x", "microphone", "Mic", "tracks/mic.flac", 48000, 1, 1000, "ab", 0, null)],
            "https://library.memento/20261006-100000-k3f9ab/mix.flac",
            "https://library.memento/20261006-100000-k3f9ab/peaks.json",
            [new Chapter("c1", 0, "Intro", "user")],
            [new Highlight("h1", 500, "", "user", null)],
            [new Topic("t1", "Budget", "local")],
            [new HistoryEntry(At, "stored", "completed", "Stored 1 track", null)],
            new IntegrityInfo("sha256", At),
            2048);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(project, BridgeJsonContext.Default.Project));
        var root = document.RootElement;
        Assert.Equal("summary,details,tracks,mixUrl,peaksUrl,chapters,highlights,topics,history,integrity,sizeBytes", Names(root));
        Assert.Equal("title,type,participants,purpose,platform,organization,location,notes,tags,agenda", Names(root.GetProperty("details")));
        Assert.Equal("""{"source":null,"parsedLocally":false,"items":[]}""", root.GetProperty("details").GetProperty("agenda").GetRawText());
        Assert.Equal(
            """{"id":"mic","sourceId":"mic:x","sourceKind":"microphone","name":"Mic","file":"tracks/mic.flac","sampleRate":48000,"channels":1,"durationMs":1000,"sha256":"ab","startOffsetMs":0,"endedEarlyAtMs":null}""",
            root.GetProperty("tracks")[0].GetRawText());
        Assert.Equal("""{"id":"c1","atMs":0,"title":"Intro","origin":"user"}""", root.GetProperty("chapters")[0].GetRawText());
        Assert.Equal("""{"id":"h1","atMs":500,"note":"","origin":"user","segmentId":null}""", root.GetProperty("highlights")[0].GetRawText());
        Assert.Equal("""{"id":"t1","label":"Budget","origin":"local"}""", root.GetProperty("topics")[0].GetRawText());
        Assert.Equal("""{"at":"2026-10-06T10:00:00+01:00","stage":"stored","event":"completed","summary":"Stored 1 track","detail":null}""", root.GetProperty("history")[0].GetRawText());
        Assert.Equal("""{"algorithm":"sha256","computedAt":"2026-10-06T10:00:00+01:00"}""", root.GetProperty("integrity").GetRawText());
    }

    [Fact]
    public void ThemeChangedEvent()
    {
        Assert.Equal(
            """{"event":"theme.changed","payload":{"isDark":true}}""",
            BridgeEventPublisher.SerializeThemeChanged(new ThemeChangedPayload(true)));
    }

    [Fact]
    public void FooterStatusEvent()
    {
        Assert.Equal(
            """{"event":"status.footer","payload":{"engine":{"ready":true,"device":"GPU","detail":{"ready":true,"device":"GPU","gpuName":"NVIDIA GeForce RTX 3060 Laptop GPU","freeVramBytes":5368709120,"model":"whisper-large-v3-turbo","paused":null}},"storage":{"freeBytes":227633266688,"lowSpace":false},"recording":{"active":true,"lastCheckpointAt":"2026-10-06T10:00:00+01:00","lostSource":"Shure MV7"},"processingPaused":null,"export":{"active":false,"percent":null,"title":null},"update":{"downloading":false,"percent":null,"version":null}}}""",
            BridgeEventPublisher.SerializeFooterStatus(
                new FooterStatusPayload(
                    new EngineStatus(true, "GPU", new EngineStatusDetail(true, "GPU", "NVIDIA GeForce RTX 3060 Laptop GPU", 5_368_709_120, "whisper-large-v3-turbo", null)),
                    new StorageStatus(227_633_266_688, false),
                    new RecordingFooterStatus(true, At, "Shure MV7"),
                    null)));
    }

    [Fact]
    public void RecordingEvents()
    {
        var track = new Track("mic", "mic:x", "microphone", "Mic", "tracks/mic.wav", 48000, 1, 1500, null, 250, null);
        Assert.Equal(
            """{"event":"recording.state","payload":{"sessionId":"s1","recordingId":"r1","state":"recording","startedAt":"2026-10-06T10:00:00+01:00","elapsedMs":1500,"tracks":[{"id":"mic","sourceId":"mic:x","sourceKind":"microphone","name":"Mic","file":"tracks/mic.wav","sampleRate":48000,"channels":1,"durationMs":1500,"sha256":null,"startOffsetMs":250,"endedEarlyAtMs":null}],"lastCheckpointAt":null,"highlightsCount":2}}""",
            BridgeEventPublisher.Serialize(BridgeEventNames.RecordingState, new RecordingStatePayload("s1", "r1", "recording", At, 1500, [track], null, 2), BridgeJsonContext.Default.BridgeEventEnvelopeRecordingStatePayload));
        Assert.Equal(
            """{"event":"recording.levels","payload":{"sessionId":"s1","levels":[{"sourceId":"mic:x","rms":0.25,"peak":0.5}]}}""",
            BridgeEventPublisher.Serialize(BridgeEventNames.RecordingLevels, new RecordingLevelsPayload("s1", [new SourceLevel("mic:x", 0.25, 0.5)]), BridgeJsonContext.Default.BridgeEventEnvelopeRecordingLevelsPayload));
        Assert.Equal(
            """{"event":"recording.sourceLost","payload":{"sessionId":"s1","sourceId":"mic:x","name":"Mic","atMs":61000,"remaining":["System"]}}""",
            BridgeEventPublisher.Serialize(BridgeEventNames.RecordingSourceLost, new SourceLostPayload("s1", "mic:x", "Mic", 61_000, ["System"]), BridgeJsonContext.Default.BridgeEventEnvelopeSourceLostPayload));
        Assert.Equal(
            """{"event":"recording.stoppedByHost","payload":{"sessionId":"s1","recordingId":"r1","reason":"diskFull","atMs":3723000,"message":"m"}}""",
            BridgeEventPublisher.Serialize(BridgeEventNames.RecordingStoppedByHost, new StoppedByHostPayload("s1", "r1", "diskFull", 3_723_000, "m"), BridgeJsonContext.Default.BridgeEventEnvelopeStoppedByHostPayload));
    }

    [Fact]
    public void LibraryAndStorageEvents()
    {
        Assert.Equal(
            """{"event":"library.changed","payload":{"recordingIds":["r1"]}}""",
            BridgeEventPublisher.Serialize(BridgeEventNames.LibraryChanged, new LibraryChangedPayload(["r1"]), BridgeJsonContext.Default.BridgeEventEnvelopeLibraryChangedPayload));
        Assert.Equal(
            """{"event":"processing.progress","payload":{"recordingId":"r1","stages":[{"stage":"stored","state":"active","percent":40,"label":"40% \u00B7 saving tracks"}]}}""",
            BridgeEventPublisher.Serialize(BridgeEventNames.ProcessingProgress, new ProcessingProgressPayload("r1", [new StageStatus("stored", "active", 40, "40% · saving tracks")]), BridgeJsonContext.Default.BridgeEventEnvelopeProcessingProgressPayload));
        Assert.Equal(
            """{"event":"storage.lowSpace","payload":{"freeBytes":5,"thresholdBytes":10,"recordingContinues":true,"transcriptionPaused":true}}""",
            BridgeEventPublisher.Serialize(BridgeEventNames.StorageLowSpace, new StorageLowSpacePayload(5, 10, true, true), BridgeJsonContext.Default.BridgeEventEnvelopeStorageLowSpacePayload));
    }

    [Fact]
    public void RecoveryItemShape()
    {
        var json = JsonSerializer.Serialize(
            new RecoveryListResult([new RecoveryItem("r1", "Sync", At, 2, 3, At, 60_000, 30_000)]),
            BridgeJsonContext.Default.RecoveryListResult);

        Assert.Equal(
            """{"items":[{"recordingId":"r1","title":"Sync","startedAt":"2026-10-06T10:00:00+01:00","tracksIntact":2,"tracksTotal":3,"lastCheckpointAt":"2026-10-06T10:00:00+01:00","recoveredDurationMs":60000,"mayBeMissingMs":30000}]}""",
            json);
    }

    [Fact]
    public void RequestEnvelopeRoundTrips()
    {
        using var parameters = JsonDocument.Parse("""{"theme":"light"}""");
        var request = new BridgeRequest(17, "settings.set", parameters.RootElement.Clone());

        var json = JsonSerializer.Serialize(request, BridgeJsonContext.Default.BridgeRequest);
        var back = JsonSerializer.Deserialize(json, BridgeJsonContext.Default.BridgeRequest)!;

        Assert.Equal("""{"id":17,"method":"settings.set","params":{"theme":"light"}}""", json);
        Assert.Equal(17, back.Id);
        Assert.Equal("settings.set", back.Method);
        Assert.Equal("light", back.Params!.Value.GetProperty("theme").GetString());
    }

    [Theory]
    [InlineData("""{"theme":"light","listDensity":"compact"}""", "light", "compact")]
    [InlineData("""{"theme":null}""", null, null)]
    [InlineData("""{}""", null, null)]
    public void SettingsSetParamsAreAllOptional(string json, string? theme, string? density)
    {
        var parameters = JsonSerializer.Deserialize(json, BridgeJsonContext.Default.SettingsSetParams)!;

        Assert.Equal(theme, parameters.Theme);
        Assert.Equal(density, parameters.ListDensity);
        Assert.Null(parameters.LibraryPath);
        Assert.Null(parameters.Recording);
    }

    [Fact]
    public void UnknownParamFieldsAreRejected()
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize("""{"theme":"light","colour":"red"}""", BridgeJsonContext.Default.SettingsSetParams));
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize("""{"anything":1}""", BridgeJsonContext.Default.EmptyParams));
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize("""{"sessionId":"s","extra":true}""", BridgeJsonContext.Default.SessionParams));
    }

    [Fact]
    public void RequiredParamsAreRequired()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("{}", BridgeJsonContext.Default.OpenExternalParams));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("{}", BridgeJsonContext.Default.RecordingIdParams));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("""{"title":"x","type":"meeting"}""", BridgeJsonContext.Default.RecordingStartParams));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("""{"sessionId":"s","sourceId":"x"}""", BridgeJsonContext.Default.RecordingSetSourceParams));
    }
}
