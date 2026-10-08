using System.Text.Json;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Tests.Fakes;

namespace Memento.Core.Tests.Bridge;

/// <summary>
/// Pins the JSON of every M2 method result and event (docs/BRIDGE.md › M2). If one fails, update
/// <c>ui/src/bridge/types.ts</c> in the same change.
/// </summary>
public sealed class M2ContractSerializationTests : IDisposable
{
    private static readonly DateTimeOffset At = new(2026, 10, 6, 10, 0, 0, TimeSpan.FromHours(1));

    private static readonly TranscriptSegment Segment = new(
        "s0001", 12.4, 15.92, "system", "spk1", 0.82, "Hello there.", 0.91,
        [new TranscriptWord("Hello", 12.4, 12.71, 0.97), new TranscriptWord("there.", 12.71, 15.92, 0.91)],
        new TranscriptEdit(At, "Hello their."));

    private static readonly Speaker Speaker = new("spk1", "Speaker 1", false, 1, 3520);

    private readonly BridgeTestHost _host = new();

    public void Dispose() => _host.Dispose();

    private static string Json<T>(T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info) => JsonSerializer.Serialize(value, info);

    [Fact]
    public void TranscriptShape()
    {
        var transcript = new Transcript(
            1, "en", true, new TranscriptEngineInfo("whisper.cpp", "whisper-large-v3-turbo", "GPU (Vulkan)", "1.9.1", 123456),
            [Speaker], [Segment], false, 4, 0.5, [new CoverageGap(30, 55.5, "mic")]);

        Assert.Equal(
            """{"transcript":{"schemaVersion":1,"language":"en","languageDetected":true,"engine":{"name":"whisper.cpp","model":"whisper-large-v3-turbo","device":"GPU (Vulkan)","version":"1.9.1","durationMs":123456},"speakers":[{"id":"spk1","name":"Speaker 1","renamed":false,"color":1,"talkTimeMs":3520}],"segments":[{"id":"s0001","start":12.4,"end":15.92,"track":"system","speaker":"spk1","speakerConfidence":0.82,"text":"Hello there.","confidence":0.91,"words":[{"w":"Hello","s":12.4,"e":12.71,"c":0.97},{"w":"there.","s":12.71,"e":15.92,"c":0.91}],"edited":{"at":"2026-10-06T10:00:00+01:00","original":"Hello their."}}],"reviewed":false,"version":4,"lowConfidenceThreshold":0.5,"coverageGaps":[{"start":30,"end":55.5,"track":"mic"}]},"status":"done","failure":null}""",
            Json(new TranscriptGetResult(transcript, "done", null), BridgeJsonContext.Default.TranscriptGetResult));
    }

    [Fact]
    public void TranscriptGetBeforeTheFirstPassAndAFailure()
    {
        Assert.Equal("""{"transcript":null,"status":"none","failure":null}""", Json(new TranscriptGetResult(null, "none", null), BridgeJsonContext.Default.TranscriptGetResult));
        Assert.Equal(
            """{"transcript":null,"status":"failed","failure":{"stage":"transcript","message":"Transcription stopped at 12:30.","kept":"The transcript up to 12:30 is kept.","remedies":[{"id":"cpu","label":"Retry on CPU"},{"id":"model:whisper-small","label":"Use the Small model"}]}}""",
            Json(
                new TranscriptGetResult(null, "failed", new StageFailure("transcript", "Transcription stopped at 12:30.", "The transcript up to 12:30 is kept.", [new Remedy("cpu", "Retry on CPU"), new Remedy("model:whisper-small", "Use the Small model")])),
                BridgeJsonContext.Default.TranscriptGetResult));
    }

    [Fact]
    public void EditAndSpeakerResults()
    {
        var plain = Segment with { Words = [], Edited = null, Speaker = null, SpeakerConfidence = null };
        Assert.Equal(
            """{"segment":{"id":"s0001","start":12.4,"end":15.92,"track":"system","speaker":null,"speakerConfidence":null,"text":"Hello there.","confidence":0.91,"words":[],"edited":null},"version":2}""",
            Json(new TranscriptSegmentResult(plain, 2), BridgeJsonContext.Default.TranscriptSegmentResult));
        Assert.Equal("""{"speakers":[{"id":"spk1","name":"Speaker 1","renamed":false,"color":1,"talkTimeMs":3520}]}""", Json(new SpeakersResult([Speaker]), BridgeJsonContext.Default.SpeakersResult));
        Assert.Equal("""{"speakers":[],"segmentsChanged":3}""", Json(new MergeSpeakersResult([], 3), BridgeJsonContext.Default.MergeSpeakersResult));
        Assert.Equal("""{"reviewed":true}""", Json(new MarkReviewedResult(true), BridgeJsonContext.Default.MarkReviewedResult));
        Assert.StartsWith("""{"segment":{"id":"s0001",""", Json(new SegmentSpeakersResult(plain, [Speaker]), BridgeJsonContext.Default.SegmentSpeakersResult), StringComparison.Ordinal);
    }

    [Fact]
    public void RestoreAndRemoveSpeakerParams()
    {
        var restore = JsonSerializer.Deserialize(
            """{"recordingId":"x","speaker":{"id":"spk2","name":"Avery","color":2,"renamed":true},"segmentIds":["s0001","s0004"]}""",
            BridgeJsonContext.Default.TranscriptRestoreSpeakerParams)!;
        Assert.Equal(new SpeakerRestore("spk2", "Avery", 2, true), restore.Speaker);
        Assert.Equal(["s0001", "s0004"], restore.SegmentIds);
        Assert.Equal(
            """{"recordingId":"x","speakerId":"spk3"}""",
            Json(new TranscriptRemoveSpeakerParams { RecordingId = "x", SpeakerId = "spk3" }, BridgeJsonContext.Default.TranscriptRemoveSpeakerParams));
    }

    [Fact]
    public void SearchAndVersionResults()
    {
        Assert.Equal(
            """{"matches":[{"segmentId":"s0003","start":9.5,"snippet":"the budget for"}]}""",
            Json(new TranscriptSearchResult([new TranscriptMatch("s0003", 9.5, "the budget for")]), BridgeJsonContext.Default.TranscriptSearchResult));
        Assert.Equal(
            """{"versions":[{"id":"20261006T090000000Z","at":"2026-10-06T10:00:00+01:00","reason":"edited","engine":"whisper.cpp small","segments":42}]}""",
            Json(new TranscriptVersionsResult([new TranscriptVersion("20261006T090000000Z", At, "edited", "whisper.cpp small", 42)]), BridgeJsonContext.Default.TranscriptVersionsResult));
    }

    [Fact]
    public void ModelsListShape()
    {
        var info = new ModelInfo("whisper-large-v3-turbo", "transcription", "Large v3 Turbo", "d", 1_624_555_275, "MIT", false, new ModelInstalling(42, 682_000_000), true, "gpu", 2_684_354_560, "Most accurate", null);
        var voice = new ModelInfo("nemo-titanet-small", "speakers", "Voice model", "d", 40_257_283, "CC-BY-4.0", true, null, true, "cpu", null, "Most accurate", "embedding");

        Assert.Equal(
            """{"models":[{"id":"whisper-large-v3-turbo","engine":"transcription","name":"Large v3 Turbo","description":"d","sizeBytes":1624555275,"license":"MIT","installed":false,"installing":{"percent":42,"bytesDone":682000000},"recommended":true,"runsOn":"gpu","minVramBytes":2684354560,"accuracyNote":"Most accurate","role":null},"""
            + """{"id":"nemo-titanet-small","engine":"speakers","name":"Voice model","description":"d","sizeBytes":40257283,"license":"CC-BY-4.0","installed":true,"installing":null,"recommended":true,"runsOn":"cpu","minVramBytes":null,"accuracyNote":"Most accurate","role":"embedding"}]}""",
            Json(new ModelsListResult([info, voice]), BridgeJsonContext.Default.ModelsListResult));
    }

    [Fact]
    public async Task ModelsListThroughTheBridgeHasEveryCatalogModelAndNothingInstalled()
    {
        var models = (await _host.ResultAsync("models.list")).GetProperty("models").EnumerateArray().ToList();

        Assert.Equal(10, models.Count);
        Assert.All(models, m => Assert.False(m.GetProperty("installed").GetBoolean()));
        Assert.All(models, m => Assert.Equal(JsonValueKind.Null, m.GetProperty("installing").ValueKind));
        Assert.Equal(["whisper-small", "pyannote-segmentation-3-0", "nemo-titanet-small", "tesseract-eng", "ministral-3-3b-q4"], models.Where(m => m.GetProperty("recommended").GetBoolean()).Select(m => m.GetProperty("id").GetString()));
        Assert.Equal(["transcription", "speakers", "ocr", "llm"], models.Select(m => m.GetProperty("engine").GetString()).Distinct());
        Assert.Equal(
            ["segmentation", "embedding", "embedding"],
            models.Where(m => m.GetProperty("engine").GetString() == "speakers").Select(m => m.GetProperty("role").GetString()));
        Assert.All(models.Where(m => m.GetProperty("engine").GetString() != "speakers"), m => Assert.Equal(JsonValueKind.Null, m.GetProperty("role").ValueKind));
    }

    [Fact]
    public void EngineStatusShape()
    {
        var memory = new GpuMemoryInfo(
            "NVIDIA GeForce RTX 3060 Laptop GPU",
            6_285_164_544,
            858_993_459,
            5_368_709_120,
            [new GpuMemoryHolderInfo("llama-server.exe", "Ollama (llama-server.exe, started by Dictation)", 5_368_709_120, false, "Dictation")],
            "The graphics card has 0.8 GB of 6 GB free. Ollama (llama-server.exe, started by Dictation) is using 5.0 GB.");
        var result = new EngineStatusResult(
            new EngineStatusDetail(true, "CPU", "NVIDIA GeForce RTX 3060 Laptop GPU", 858_993_459, "whisper-large-v3-turbo", null) { GpuMemory = memory, Note = "The graphics card has 0.8 GB of 6 GB free." },
            new EngineStatusDetail(false, null, null, null, "nemo-titanet-small", "PC is busy"));

        Assert.Equal(
            """{"transcription":{"ready":true,"device":"CPU","gpuName":"NVIDIA GeForce RTX 3060 Laptop GPU","freeVramBytes":858993459,"model":"whisper-large-v3-turbo","paused":null,"gpuMemory":{"gpuName":"NVIDIA GeForce RTX 3060 Laptop GPU","totalBytes":6285164544,"freeBytes":858993459,"usedBytes":5368709120,"holders":[{"processName":"llama-server.exe","description":"Ollama (llama-server.exe, started by Dictation)","bytes":5368709120,"memento":false,"startedBy":"Dictation"}],"summary":"The graphics card has 0.8 GB of 6 GB free. Ollama (llama-server.exe, started by Dictation) is using 5.0 GB."},"note":"The graphics card has 0.8 GB of 6 GB free."},"speakers":{"ready":false,"device":null,"gpuName":null,"freeVramBytes":null,"model":"nemo-titanet-small","paused":"PC is busy","gpuMemory":null,"note":null}}""",
            Json(result, BridgeJsonContext.Default.EngineStatusResult));
    }

    [Fact]
    public async Task EngineStatusThroughTheBridge()
    {
        _host.Probe.Snapshot = FakeResourceProbe.WithGpu(5L << 30);

        var result = await _host.ResultAsync("engine.status");

        Assert.Equal("transcription,speakers", string.Join(",", result.EnumerateObject().Select(p => p.Name)));
        var transcription = result.GetProperty("transcription");
        Assert.False(transcription.GetProperty("ready").GetBoolean());
        Assert.Equal("whisper-large-v3-turbo", transcription.GetProperty("model").GetString());
        Assert.Equal("NVIDIA GeForce RTX 3060 Laptop GPU", transcription.GetProperty("gpuName").GetString());
        Assert.Equal(5L << 30, transcription.GetProperty("freeVramBytes").GetInt64());
    }

    [Fact]
    public void Events()
    {
        Assert.Equal(
            """{"event":"transcript.changed","payload":{"recordingId":"r1","version":3,"reason":"edited"}}""",
            BridgeEventPublisher.Serialize(BridgeEventNames.TranscriptChanged, new TranscriptChangedPayload("r1", 3, "edited"), BridgeJsonContext.Default.BridgeEventEnvelopeTranscriptChangedPayload));
        Assert.Equal(
            """{"event":"models.progress","payload":{"modelId":"whisper-small","percent":42,"bytesDone":204800000,"bytesTotal":487601967,"state":"downloading","message":null}}""",
            BridgeEventPublisher.Serialize(BridgeEventNames.ModelsProgress, new ModelsProgressPayload("whisper-small", 42, 204_800_000, 487_601_967, "downloading", null), BridgeJsonContext.Default.BridgeEventEnvelopeModelsProgressPayload));
        Assert.Equal(
            """{"event":"recording.liveTranscript","payload":{"sessionId":"s1","segments":[{"start":0,"end":10,"text":"Rough draft."}]}}""",
            BridgeEventPublisher.Serialize(BridgeEventNames.RecordingLiveTranscript, new LiveTranscriptPayload("s1", [new LiveTranscriptSegment(0, 10, "Rough draft.")]), BridgeJsonContext.Default.BridgeEventEnvelopeLiveTranscriptPayload));
    }

    [Fact]
    public async Task ProcessingPauseAndResumeAnswerEmptyAndDriveTheFooter()
    {
        Assert.Equal("""{"id":7,"result":{}}""", (await _host.CallAsync("processing.pause")).GetRawText());
        Assert.Equal("Paused by you", (await _host.ResultAsync("status.get")).GetProperty("processingPaused").GetString());
        Assert.Equal("""{"id":7,"result":{}}""", (await _host.CallAsync("processing.resume")).GetRawText());
        Assert.Equal(JsonValueKind.Null, (await _host.ResultAsync("status.get")).GetProperty("processingPaused").ValueKind);
    }

    [Theory]
    [InlineData("transcript.get", """{"recordingId":"x","extra":1}""")]
    [InlineData("transcript.editSegment", """{"recordingId":"x","segmentId":"s"}""")]
    [InlineData("transcript.setSegmentSpeaker", """{"recordingId":"x"}""")]
    [InlineData("transcript.renameSpeaker", """{"recordingId":"x","speakerId":"spk1"}""")]
    [InlineData("transcript.mergeSpeakers", """{"recordingId":"x","fromSpeakerId":"a"}""")]
    [InlineData("transcript.restoreSpeaker", """{"recordingId":"x","speaker":{"id":"spk2","name":"Avery","color":2,"renamed":true}}""")]
    [InlineData("transcript.restoreSpeaker", """{"recordingId":"x","speaker":{"id":"spk2"},"segmentIds":[]}""")]
    [InlineData("transcript.removeSpeaker", """{"recordingId":"x"}""")]
    [InlineData("transcript.reduceSpeakers", """{"recordingId":"x"}""")]
    [InlineData("transcript.reduceSpeakers", """{"recordingId":"x","count":"two"}""")]
    [InlineData("transcript.restoreSpeakers", """{"recordingId":"x"}""")]
    [InlineData("transcript.restoreSpeakers", """{"recordingId":"x","speakers":[{"speaker":{"id":"spk2","name":"Avery","color":2,"renamed":true}}]}""")]
    [InlineData("transcript.markReviewed", """{"recordingId":"x"}""")]
    [InlineData("transcript.search", """{"recordingId":"x"}""")]
    [InlineData("transcript.retranscribe", """{"modelId":"whisper-small"}""")]
    [InlineData("transcript.versions", """{}""")]
    [InlineData("transcript.restoreVersion", """{"recordingId":"x"}""")]
    [InlineData("processing.retry", """{"recordingId":"x"}""")]
    [InlineData("processing.cancel", """{"stage":"transcript"}""")]
    [InlineData("processing.pause", """{"now":true}""")]
    [InlineData("models.install", """{}""")]
    [InlineData("models.cancelInstall", """{"id":"whisper-small"}""")]
    [InlineData("models.remove", """{}""")]
    [InlineData("engine.status", """{"verbose":true}""")]
    public async Task MalformedParamsAreRejected(string method, string parameters)
    {
        var response = await _host.CallAsync(method, parameters);

        Assert.Equal("bridge.invalidParams", response.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task ModelMethodsNameTheModel()
    {
        Assert.Equal("models.notFound", (await _host.CallAsync("models.install", """{"modelId":"gpt-9"}""")).GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("models.notFound", (await _host.CallAsync("models.remove", """{"modelId":"gpt-9"}""")).GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("""{"id":7,"result":{}}""", (await _host.CallAsync("models.cancelInstall", """{"modelId":"whisper-small"}""")).GetRawText());
    }
}
