using System.Text.Json;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Models;
using Memento.Core.Processing;
using Memento.Core.Projects;
using Memento.Core.Tests.Fakes;
using Memento.Core.Transcripts;
using Memento.Core.Workers;
using Microsoft.Extensions.DependencyInjection;
using static Memento.Core.Tests.Fakes.TestRecordings;

namespace Memento.Transcription.Tests;

/// <summary>
/// The transcript and speakers stages through the real orchestrator and bridge, with a scripted worker in place of
/// Memento.Worker.exe and a catalog of tiny model files.
/// </summary>
public sealed class StageTests : IDisposable
{
    private static readonly ModelCatalog TinyCatalog = ModelCatalog.Parse("""
        { "schemaVersion": 1, "models": [
          { "id": "large-v3-turbo", "engine": "whisper", "kind": "transcription", "name": "Large v3 Turbo", "description": "d", "fileName": "ggml-large-v3-turbo.bin", "sizeBytes": 4, "sha256": "0000000000000000000000000000000000000000000000000000000000000000", "url": "https://example.org/t", "license": "MIT", "runsOn": "gpu", "minVramBytes": 2684354560, "recommendedFor": "gpu", "accuracyNote": "Most accurate" },
          { "id": "small", "engine": "whisper", "kind": "transcription", "name": "Small", "description": "d", "fileName": "ggml-small.bin", "sizeBytes": 4, "sha256": "0000000000000000000000000000000000000000000000000000000000000000", "url": "https://example.org/s", "license": "MIT", "runsOn": "either", "minVramBytes": 1073741824, "recommendedFor": "cpu", "accuracyNote": "Fast on CPU" },
          { "id": "pyannote-segmentation-3-0", "engine": "sherpa-onnx", "kind": "speakers", "role": "segmentation", "name": "Segmentation", "description": "d", "fileName": "seg.onnx", "sizeBytes": 4, "sha256": "0000000000000000000000000000000000000000000000000000000000000000", "url": "https://example.org/p", "license": "MIT", "runsOn": "cpu", "recommendedFor": "any", "accuracyNote": "Required" },
          { "id": "nemo-titanet-small", "engine": "sherpa-onnx", "kind": "speakers", "role": "embedding", "name": "TitaNet", "description": "d", "fileName": "emb.onnx", "sizeBytes": 4, "sha256": "0000000000000000000000000000000000000000000000000000000000000000", "url": "https://example.org/e", "license": "CC-BY-4.0", "runsOn": "cpu", "recommendedFor": "any", "accuracyNote": "Most accurate" }
        ] }
        """);

    private static readonly WorkerDevice Gpu = new("vulkan", "GPU (Vulkan)", "NVIDIA GeForce RTX 3060 Laptop GPU", 0, "1.9.1");

    private readonly BridgeTestHost _host;
    private readonly List<WorkerJob> _jobs = [];

    public StageTests()
    {
        _host = new BridgeTestHost(configure: services =>
        {
            services.AddSingleton(TinyCatalog);
            services.AddMementoTranscription();
        });
        _host.Probe.Snapshot = FakeResourceProbe.WithGpu(5L << 30);
        _host.Workers.Script = DefaultScript;
    }

    public void Dispose() => _host.Dispose();

    /// <summary>What the scripted transcription answers per track: windows of segments; null marks a silent track.</summary>
    private Dictionary<string, List<WorkerSegment>[]?> Windows { get; } = new(StringComparer.Ordinal)
    {
        ["mic"] =
        [
            [new WorkerSegment(0.2, 1.0, "We approve the marketing budget.", 0.8, [new TranscriptWord("We", 0.2, 0.3, 0.3), new TranscriptWord("approve", 0.3, 0.6, 0.9)])],
            [new WorkerSegment(1.2, 1.8, "And the marketing budget for Berlin.", 0.9, [])],
        ],
        ["system"] = null,
    };

    /// <summary>Called after each transcription window is sent; return false to stop there (the worker "dies").</summary>
    private Func<int, bool> AfterWindow { get; set; } = _ => true;

    private List<double[]> Speech { get; set; } = [[0.2, 1.8]];

    private async Task<int> DefaultScript(WorkerJob job, ScriptedWorkerContext context, CancellationToken cancel)
    {
        lock (_jobs)
        {
            _jobs.Add(job);
        }

        context.Send(new WorkerReply { Type = WorkerMessageTypes.Ready, Pid = 1 });
        if (job.Kind == WorkerJobKinds.Diarize)
        {
            context.Send(new WorkerReply { Type = WorkerMessageTypes.Progress, Percent = 50 });
            context.Send(new WorkerReply
            {
                Type = WorkerMessageTypes.Result,
                Diarization = new DiarizeResult([new DiarizedTrack("mic", [new SpeakerTurn(0, 1.1, 0, 0.7), new SpeakerTurn(1.1, 2, 1, 0.65)])], 2, 100),
            });
            return 0;
        }

        var sent = 0;
        foreach (var track in job.Transcribe!.Tracks)
        {
            var windows = Windows.GetValueOrDefault(track.Id);
            context.Send(new WorkerReply { Type = WorkerMessageTypes.Track, Track = new WorkerTrackInfo(track.Id, 2, windows is null, 0.1, windows is null ? [] : Speech, windows?.Length ?? 1) });
        }

        context.Send(new WorkerReply { Type = WorkerMessageTypes.Device, Device = Gpu });
        foreach (var track in job.Transcribe.Tracks)
        {
            if (Windows.GetValueOrDefault(track.Id) is not { } windows)
            {
                continue;
            }

            for (var i = track.StartWindow; i < windows.Length; i++)
            {
                cancel.ThrowIfCancellationRequested();
                await Task.Delay(5, CancellationToken.None);
                context.Send(new WorkerReply { Type = WorkerMessageTypes.Progress, Percent = 100.0 * (i + 1) / windows.Length, TrackId = track.Id, WindowsDone = i + 1, Segments = windows[i], Language = "en" });
                sent++;
                if (!AfterWindow(sent))
                {
                    return unchecked((int)0xC0000409);
                }
            }
        }

        context.Send(new WorkerReply { Type = WorkerMessageTypes.Result, Transcription = new TranscribeResult("en", true, Gpu, 4, 250) });
        return 0;
    }

    private void Install(params string[] ids)
    {
        foreach (var id in ids)
        {
            var entry = TinyCatalog.Find(id)!;
            var path = _host.Models.PathOf(entry);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, new byte[entry.SizeBytes]);
        }
    }

    private void InstallAll() => Install("large-v3-turbo", "small", "pyannote-segmentation-3-0", "nemo-titanet-small");

    private async Task<string> RecordAndProcessAsync()
    {
        var id = await _host.RecordAsync("Budget meeting", 2, Mic, SystemAudio);
        await IdleAsync();
        return id;
    }

    private async Task IdleAsync()
    {
        await Task.Delay(50);
        await _host.Processing.WhenIdleAsync();
    }

    private Task<ProjectManifest> ManifestAsync(string id) => _host.Store.LoadAsync(id, CancellationToken.None);

    private async Task<TranscriptDocument> TranscriptAsync(string id) => (await _host.Transcripts.LoadAsync(id, CancellationToken.None))!;

    private static StageStatus Stage(ProjectManifest manifest, string name) => Assert.Single(manifest.Stages, s => s.Stage == name);

    private List<WorkerJob> Jobs(string kind)
    {
        lock (_jobs)
        {
            return _jobs.Where(j => j.Kind == kind).ToList();
        }
    }

    [Fact]
    public async Task ARecordingIsTranscribedPerTrackThenSpeakersAndTopics()
    {
        InstallAll();

        var id = await RecordAndProcessAsync();

        var manifest = await ManifestAsync(id);
        Assert.Equal([StageNames.Stored, StageNames.Transcript, StageNames.Speakers], manifest.Stages.Select(s => s.Stage));
        Assert.All(manifest.Stages, s => Assert.Equal(StageStates.Done, s.State));
        Assert.Null(manifest.Processing);

        var transcript = await TranscriptAsync(id);
        Assert.True(transcript.Complete);
        Assert.Equal(["s0001", "s0002"], transcript.Segments.Select(s => s.Id));
        Assert.All(transcript.Segments, s => Assert.Equal("mic", s.Track));
        Assert.Equal(("whisper.cpp", "large-v3-turbo", "GPU (Vulkan)", "1.9.1"), (transcript.Engine.Name, transcript.Engine.Model, transcript.Engine.Device, transcript.Engine.Version));
        Assert.Equal("en", transcript.Language);
        Assert.True(transcript.LanguageDetected);
        Assert.Equal(0.5, transcript.LowConfidenceThreshold);
        Assert.Equal(["spk1", "spk2"], transcript.Segments.Select(s => s.Speaker));
        Assert.Equal(2, transcript.Speakers.Count);
        Assert.Equal(2, transcript.Version);
        Assert.False(File.Exists(Path.Combine(_host.Store.GetProjectFolder(id), ProjectLayout.TranscriptPartialFile)));

        // The job: both tracks from the lossless files, Vulkan then CPU on the discrete GPU, prompt, windows.
        var job = Assert.Single(Jobs(WorkerJobKinds.Transcribe)).Transcribe!;
        Assert.Equal(["mic", "system"], job.Tracks.Select(t => t.Id));
        Assert.All(job.Tracks, t => Assert.True(File.Exists(t.Path)));
        Assert.Equal(["vulkan", "cpu"], job.Runtimes);
        Assert.Equal("NVIDIA GeForce RTX 3060 Laptop GPU", job.GpuName);
        Assert.Equal(-1, job.GpuDevice);
        Assert.Equal("auto", job.Language);
        Assert.Equal(TranscriptionDefaults.Prompt, job.Prompt);
        Assert.Equal((600.0, 5.0), (job.WindowSeconds, job.OverlapSeconds));
        Assert.True(job.WordTimestamps);
        var diarize = Assert.Single(Jobs(WorkerJobKinds.Diarize)).Diarize!;
        Assert.Equal(["mic"], diarize.Tracks.Select(t => t.Id));
        Assert.Equal((-1, 0.8f, 4), (diarize.NumClusters, diarize.Threshold, diarize.Threads));

        var history = await _host.Store.ReadHistoryAsync(id, CancellationToken.None);
        Assert.Contains(history, h => h.Stage == "transcript" && h.Event == "started");
        var completed = Assert.Single(history, h => h.Stage == "transcript" && h.Event == "completed");
        Assert.Contains("Large v3 Turbo", completed.Detail, StringComparison.Ordinal);
        Assert.Contains("2 segments", completed.Detail, StringComparison.Ordinal);
        Assert.Contains(history, h => h.Stage == "transcript" && h.Event == "info" && h.Summary.StartsWith("Skipped", StringComparison.Ordinal));
        Assert.Contains(history, h => h.Stage == "topics" && h.Event == "completed");
        Assert.Contains(history, h => h.Stage == "speakers" && h.Event == "completed" && h.Summary == "Found 2 speakers");

        var annotations = await _host.Store.LoadAnnotationsAsync(id, CancellationToken.None);
        Assert.Contains(annotations.Topics, t => t.Label == "Marketing budget" && t.Origin == "local");
        await _host.Sink.WaitForAsync("transcript.changed", p => p.GetProperty("reason").GetString() == "speakers");
        await _host.Sink.WaitForAsync("transcript.changed", p => p.GetProperty("reason").GetString() == "topics");
        var get = await _host.ResultAsync("transcript.get", JsonSerializer.Serialize(new { recordingId = id }));
        Assert.Equal("done", get.GetProperty("status").GetString());
        Assert.Single((await _host.ResultAsync("library.list", """{"query":"berlin"}""")).GetProperty("recordings").EnumerateArray());
    }

    [Fact]
    public async Task SpeechWithoutTranscriptIsFlaggedAsACoverageGap()
    {
        InstallAll();
        Speech = [[0.2, 1.8], [30, 60]];

        var id = await RecordAndProcessAsync();

        var gap = Assert.Single((await TranscriptAsync(id)).CoverageGaps);
        Assert.Equal(("mic", 30.0, 60.0), (gap.Track, gap.Start, gap.End));
        var history = await _host.Store.ReadHistoryAsync(id, CancellationToken.None);
        Assert.Contains(history, h => h.Stage == "transcript" && h.Event == "info" && h.Summary == "Speech without a transcript at 0:30–1:00");
    }

    [Fact]
    public async Task AWorkerThatDiesIsReportedWithRemediesKeepsThePartAndRetryOnCpuContinues()
    {
        InstallAll();
        AfterWindow = sent => sent < 1;

        var id = await RecordAndProcessAsync();

        var manifest = await ManifestAsync(id);
        Assert.Equal(StageStates.Failed, Stage(manifest, StageNames.Transcript).State);
        Assert.DoesNotContain(manifest.Stages, s => s.Stage == StageNames.Speakers);
        var failure = Assert.Single(manifest.Failures);
        Assert.Equal(ProjectStageFailure.CauseCrashed, failure.Cause);
        Assert.StartsWith("Transcription stopped at 0:01: the engine stopped while using the graphics card (code 0xC0000409).", failure.Message, StringComparison.Ordinal);
        Assert.Contains("transcript up to 0:01 is kept", failure.Kept, StringComparison.Ordinal);
        Assert.Equal(["cpu", "model:small", "retry"], failure.Remedies.Select(r => r.Id));
        var partial = await TranscriptAsync(id);
        Assert.False(partial.Complete);
        Assert.Equal("We approve the marketing budget.", Assert.Single(partial.Segments).Text);
        var get = await _host.ResultAsync("transcript.get", JsonSerializer.Serialize(new { recordingId = id }));
        Assert.Equal("failed", get.GetProperty("status").GetString());
        Assert.Equal(1, get.GetProperty("transcript").GetProperty("segments").GetArrayLength());

        AfterWindow = _ => true;
        await _host.ResultAsync("processing.retry", JsonSerializer.Serialize(new { recordingId = id, stage = "transcript", remedyId = "cpu" }));
        await IdleAsync();

        var retry = Jobs(WorkerJobKinds.Transcribe)[^1].Transcribe!;
        Assert.Equal(["cpu"], retry.Runtimes);
        Assert.Equal(1, retry.Tracks.Single(t => t.Id == "mic").StartWindow);
        var finished = await TranscriptAsync(id);
        Assert.True(finished.Complete);
        Assert.Equal(["We approve the marketing budget.", "And the marketing budget for Berlin."], finished.Segments.Select(s => s.Text));
        manifest = await ManifestAsync(id);
        Assert.Equal(StageStates.Done, Stage(manifest, StageNames.Transcript).State);
        Assert.Equal(StageStates.Done, Stage(manifest, StageNames.Speakers).State);
        Assert.Empty(manifest.Failures);
    }

    [Fact]
    public async Task AModelThatWillNotLoadOffersTheSmallerModelFirst()
    {
        InstallAll();
        _host.Workers.Script = (_, context, _) =>
        {
            context.Send(new WorkerReply { Type = WorkerMessageTypes.Error, Code = WorkerErrorCodes.ModelLoad, Message = "invalid model file" });
            return Task.FromResult(1);
        };

        var id = await RecordAndProcessAsync();

        var failure = Assert.Single((await ManifestAsync(id)).Failures);
        Assert.Equal("Transcription could not start: the Large v3 Turbo model could not be loaded (invalid model file).", failure.Message);
        Assert.Equal(["model:small", "cpu", "retry"], failure.Remedies.Select(r => r.Id));
        Assert.Null(await _host.Transcripts.LoadAsync(id, CancellationToken.None));
    }

    [Fact]
    public async Task WithoutTheModelTheStageWaitsWithASpecificMessage()
    {
        Install("small");

        var id = await RecordAndProcessAsync();

        var failure = Assert.Single((await ManifestAsync(id)).Failures);
        Assert.Equal(ProjectStageFailure.CauseNoModel, failure.Cause);
        Assert.Equal("Transcription needs the Large v3 Turbo model, and it is not installed.", failure.Message);
        Assert.Equal(["model:small", "retry"], failure.Remedies.Select(r => r.Id));
        Assert.Equal("Waiting for a model", Stage(await ManifestAsync(id), StageNames.Transcript).Label);
        Assert.Empty(Jobs(WorkerJobKinds.Transcribe));

        await _host.ResultAsync("processing.retry", JsonSerializer.Serialize(new { recordingId = id, stage = "transcript", remedyId = "model:small" }));
        await IdleAsync();

        Assert.Equal("small", (await TranscriptAsync(id)).Engine.Model);
    }

    [Fact]
    public async Task APauseStopsTheWorkerAndThePassResumesFromTheNextWindow()
    {
        InstallAll();
        var firstWindow = new TaskCompletionSource();
        AfterWindow = sent =>
        {
            if (sent == 1)
            {
                firstWindow.TrySetResult();
                Thread.Sleep(300);
            }

            return true;
        };

        var id = await _host.RecordAsync("Paused pass", 2, Mic, SystemAudio);
        await firstWindow.Task.WaitAsync(TimeSpan.FromSeconds(10));
        AfterWindow = _ => true;
        _host.Gate.SetManual(true);
        await WaitUntilAsync(async () => Stage(await ManifestAsync(id), StageNames.Transcript).Label == "Paused · Paused by you", "the pass to pause");
        _host.Gate.SetManual(false);
        await IdleAsync();

        var jobs = Jobs(WorkerJobKinds.Transcribe);
        Assert.True(jobs.Count >= 2);
        Assert.Equal(1, jobs[^1].Transcribe!.Tracks.Single(t => t.Id == "mic").StartWindow);
        Assert.Equal(2, (await TranscriptAsync(id)).Segments.Count);
    }

    [Fact]
    public async Task CancellingKeepsWhatWasTranscribed()
    {
        InstallAll();
        var firstWindow = new TaskCompletionSource();
        _host.Workers.Script = async (job, context, cancel) =>
        {
            context.Send(new WorkerReply { Type = WorkerMessageTypes.Track, Track = new WorkerTrackInfo("mic", 2, false, 0.1, [[0.2, 1.8]], 2) });
            context.Send(new WorkerReply { Type = WorkerMessageTypes.Progress, Percent = 50, TrackId = "mic", WindowsDone = 1, Segments = Windows["mic"]![0] });
            firstWindow.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancel);
            return 0;
        };

        var id = await _host.RecordAsync("Cancelled pass", 2, Mic);
        await firstWindow.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Task.Delay(100);
        await _host.ResultAsync("processing.cancel", JsonSerializer.Serialize(new { recordingId = id, stage = "transcript" }));
        await IdleAsync();

        var failure = Assert.Single((await ManifestAsync(id)).Failures);
        Assert.Equal(ProjectStageFailure.CauseCancelled, failure.Cause);
        Assert.StartsWith("Transcription was cancelled at 0:01.", failure.Message, StringComparison.Ordinal);
        Assert.Equal("continue transcribing", failure.Remedies[0].Label.ToLowerInvariant());
        Assert.Single((await TranscriptAsync(id)).Segments);
    }

    [Fact]
    public async Task RetranscribingKeepsTheEarlierTranscriptAsAVersion()
    {
        InstallAll();
        var id = await RecordAndProcessAsync();
        await _host.ResultAsync("transcript.editSegment", JsonSerializer.Serialize(new { recordingId = id, segmentId = "s0001", text = "We approve it." }));

        await _host.ResultAsync("transcript.retranscribe", JsonSerializer.Serialize(new { recordingId = id, modelId = "small", language = "en" }));
        await IdleAsync();

        var transcript = await TranscriptAsync(id);
        Assert.Equal("small", transcript.Engine.Model);
        Assert.Equal(TranscriptChangeReasons.Retranscribed, transcript.LastChange!.Reason);
        Assert.Equal("en", Jobs(WorkerJobKinds.Transcribe)[^1].Transcribe!.Language);
        var versions = (await _host.ResultAsync("transcript.versions", JsonSerializer.Serialize(new { recordingId = id }))).GetProperty("versions").EnumerateArray().ToList();
        Assert.Equal("edited", versions[0].GetProperty("reason").GetString());
        Assert.Equal("We approve it.", (await _host.Transcripts.LoadVersionAsync(id, versions[0].GetProperty("id").GetString()!, CancellationToken.None))!.Transcript.Segments[0].Text);
        await _host.Sink.WaitForAsync("transcript.changed", p => p.GetProperty("reason").GetString() == "transcribed" && p.GetProperty("version").GetInt32() > 3);
    }

    [Fact]
    public async Task AMissingWorkerIsReportedAsSuch()
    {
        InstallAll();
        _host.Workers.StartFailure = new WorkerUnavailableException("missing");

        var id = await RecordAndProcessAsync();

        var failure = Assert.Single((await ManifestAsync(id)).Failures);
        Assert.Equal(ProjectStageFailure.CauseNoWorker, failure.Cause);
        Assert.Contains("worker is missing", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithoutTheSpeakerModelsTheTranscriptIsKeptWithoutSpeakers()
    {
        Install("large-v3-turbo");

        var id = await RecordAndProcessAsync();

        var manifest = await ManifestAsync(id);
        Assert.Equal(StageStates.Done, Stage(manifest, StageNames.Transcript).State);
        Assert.Equal(StageStates.Failed, Stage(manifest, StageNames.Speakers).State);
        var failure = Assert.Single(manifest.Failures);
        Assert.Equal(StageNames.Speakers, failure.Stage);
        Assert.Equal("Speaker identification needs Segmentation and TitaNet, and they are not installed.", failure.Message);
        Assert.All((await TranscriptAsync(id)).Segments, s => Assert.Null(s.Speaker));
    }

    [Fact]
    public async Task TheExpectedSpeakerCountIsUsedForASingleSpokenTrack()
    {
        InstallAll();
        await _host.ResultAsync("settings.set", """{"speakers":{"expectedSpeakers":3,"rememberRenamed":true}}""");

        var id = await RecordAndProcessAsync();

        Assert.Equal(3, Assert.Single(Jobs(WorkerJobKinds.Diarize)).Diarize!.NumClusters);
        var history = await _host.Store.ReadHistoryAsync(id, CancellationToken.None);
        Assert.Contains(history, h => h.Stage == "speakers" && h.Summary == "Renamed speakers are not remembered yet");
    }

    [Fact]
    public async Task TurningTranscriptionOffQueuesNothing()
    {
        InstallAll();
        await _host.ResultAsync("settings.set", """{"transcription":{"auto":false}}""");

        var id = await RecordAndProcessAsync();

        Assert.Equal([StageNames.Stored], (await ManifestAsync(id)).Stages.Select(s => s.Stage));
        Assert.Empty(Jobs(WorkerJobKinds.Transcribe));
    }
}
