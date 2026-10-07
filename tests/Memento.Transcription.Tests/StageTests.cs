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
    private static readonly ModelCatalog TinyCatalog = TestCatalogs.Tiny;

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

    private void Install(params string[] ids) => TestCatalogs.Install(_host, ids);

    private void InstallAll() => Install("whisper-large-v3-turbo", "whisper-small", "pyannote-segmentation-3-0", "nemo-titanet-small");

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
        Assert.Equal([StageNames.Stored, StageNames.Transcript, StageNames.Speakers, StageNames.Topics], manifest.Stages.Select(s => s.Stage));
        Assert.All(manifest.Stages, s => Assert.Equal(StageStates.Done, s.State));
        Assert.Null(manifest.Processing);

        var transcript = await TranscriptAsync(id);
        Assert.True(transcript.Complete);
        Assert.Equal(["s0001", "s0002"], transcript.Segments.Select(s => s.Id));
        Assert.All(transcript.Segments, s => Assert.Equal("mic", s.Track));
        Assert.Equal(("whisper.cpp", "whisper-large-v3-turbo", "GPU (Vulkan)", "1.9.1"), (transcript.Engine.Name, transcript.Engine.Model, transcript.Engine.Device, transcript.Engine.Version));
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
    public async Task StoringMarksTheFollowingStagesQueuedInTheSameWriteSoTheCardNeverBlinksOut()
    {
        InstallAll();

        await RecordAndProcessAsync();

        // The first progress that has stored done already lists the stages after it as queued.
        var storedDone = await _host.Sink.WaitForAsync(
            "processing.progress",
            p => p.GetProperty("stages").EnumerateArray().Any(s => s.GetProperty("stage").GetString() == "stored" && s.GetProperty("state").GetString() == "done"));
        Assert.Equal(
            ["stored:done", "transcript:queued", "speakers:queued", "topics:queued"],
            storedDone.GetProperty("stages").EnumerateArray().Select(s => $"{s.GetProperty("stage").GetString()}:{s.GetProperty("state").GetString()}"));
    }

    [Fact]
    public async Task AFinishedTopicsStageIsLeftOutOfTheLibraryRowLikeStored()
    {
        InstallAll();

        await RecordAndProcessAsync();

        var row = Assert.Single((await _host.ResultAsync("library.list")).GetProperty("recordings").EnumerateArray());
        Assert.Equal(["transcript", "speakers"], row.GetProperty("stages").EnumerateArray().Select(s => s.GetProperty("stage").GetString()));
    }

    [Fact]
    public async Task HighlightsPointAtTheirLineOnceThereIsATranscript()
    {
        InstallAll();
        var (sessionId, id) = await _host.StartAsync("Highlights", Mic, SystemAudio);
        _host.Session.Advance(TimeSpan.FromSeconds(1.5));
        await _host.ResultAsync("recording.markHighlight", JsonSerializer.Serialize(new { sessionId }));
        _host.Session.Advance(TimeSpan.FromSeconds(0.5));
        await _host.ResultAsync("recording.stop", JsonSerializer.Serialize(new { sessionId }));
        await _host.Recordings.WhenIdleAsync();
        await IdleAsync();

        var marked = Assert.Single((await _host.Store.LoadAnnotationsAsync(id, CancellationToken.None)).Highlights);
        Assert.Equal("s0002", marked.SegmentId);

        var added = await _host.ResultAsync("annotations.addHighlight", JsonSerializer.Serialize(new { recordingId = id, highlight = new { atMs = 300 } }));
        Assert.All(added.GetProperty("highlights").EnumerateArray(), h => Assert.NotEqual(JsonValueKind.Null, h.GetProperty("segmentId").ValueKind));
        Assert.Contains(added.GetProperty("highlights").EnumerateArray(), h => h.GetProperty("segmentId").GetString() == "s0001");

        var moved = await _host.ResultAsync("annotations.updateHighlight", JsonSerializer.Serialize(new { recordingId = id, highlight = new { id = marked.Id, atMs = 100 } }));
        Assert.Equal("s0001", moved.GetProperty("highlights").EnumerateArray().Single(h => h.GetProperty("id").GetString() == marked.Id).GetProperty("segmentId").GetString());
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
        Assert.Equal(["cpu", "model:whisper-small", "retry"], failure.Remedies.Select(r => r.Id));
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
        Assert.Equal(["model:whisper-small", "cpu", "retry"], failure.Remedies.Select(r => r.Id));
        Assert.Null(await _host.Transcripts.LoadAsync(id, CancellationToken.None));
    }

    [Fact]
    public async Task WithoutTheModelTheStageWaitsWithASpecificMessage()
    {
        Install("whisper-small");

        var id = await RecordAndProcessAsync();

        var failure = Assert.Single((await ManifestAsync(id)).Failures);
        Assert.Equal(ProjectStageFailure.CauseNoModel, failure.Cause);
        Assert.Equal("Transcription needs the Large v3 Turbo model, and it is not installed.", failure.Message);
        Assert.Equal(["model:whisper-small", "retry"], failure.Remedies.Select(r => r.Id));
        Assert.Equal("Waiting for a model", Stage(await ManifestAsync(id), StageNames.Transcript).Label);
        Assert.Empty(Jobs(WorkerJobKinds.Transcribe));

        await _host.ResultAsync("processing.retry", JsonSerializer.Serialize(new { recordingId = id, stage = "transcript", remedyId = "model:whisper-small" }));
        await IdleAsync();

        Assert.Equal("whisper-small", (await TranscriptAsync(id)).Engine.Model);
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

        await _host.ResultAsync("transcript.retranscribe", JsonSerializer.Serialize(new { recordingId = id, modelId = "whisper-small", language = "en" }));
        await IdleAsync();

        var transcript = await TranscriptAsync(id);
        Assert.Equal("whisper-small", transcript.Engine.Model);
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
        Install("whisper-large-v3-turbo");

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
    public async Task ALineTheEngineRepeatsIsDroppedAndHistorySaysSo()
    {
        InstallAll();
        Windows["mic"] =
        [
            [
                new WorkerSegment(0.2, 1.0, "We approve the marketing budget.", 0.8, []),
                new WorkerSegment(1.0, 1.2, "Thank you.", 0.4, []),
                new WorkerSegment(1.2, 1.4, "Thank you.", 0.4, []),
                new WorkerSegment(1.4, 1.6, "Thank you.", 0.4, []),
            ],
        ];

        var id = await RecordAndProcessAsync();

        Assert.Equal(["We approve the marketing budget.", "Thank you."], (await TranscriptAsync(id)).Segments.Select(s => s.Text));
        var history = await _host.Store.ReadHistoryAsync(id, CancellationToken.None);
        var dropped = Assert.Single(history, h => h.Stage == "transcript" && h.Event == "info" && h.Summary == "Dropped 2 repeated lines");
        Assert.StartsWith("“Thank you.” 3 times in a row at 0:01–0:01 on ", dropped.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AResumedPassNeverKeepsAWindowTwiceEvenIfTheWorkerSendsItAgain()
    {
        InstallAll();
        AfterWindow = sent => sent < 1;
        var id = await RecordAndProcessAsync();
        Assert.Single((await TranscriptAsync(id)).Segments);

        // A worker that ignores the resume point and sends every window again.
        AfterWindow = _ => true;
        var inner = _host.Workers.Script;
        _host.Workers.Script = (job, context, cancel) => inner(
            job.Transcribe is { } transcribe ? job with { Transcribe = transcribe with { Tracks = transcribe.Tracks.Select(t => t with { StartWindow = 0 }).ToList() } } : job,
            context,
            cancel);
        await _host.ResultAsync("processing.retry", JsonSerializer.Serialize(new { recordingId = id, stage = "transcript", remedyId = "retry" }));
        await IdleAsync();

        var finished = await TranscriptAsync(id);
        Assert.True(finished.Complete);
        Assert.Equal(["We approve the marketing budget.", "And the marketing budget for Berlin."], finished.Segments.Select(s => s.Text));
    }

    [Fact]
    public async Task AWorkerKilledInsideTheOnlyWindowStartsThatWindowAgainOnce()
    {
        InstallAll();
        Windows["mic"] = [[new WorkerSegment(0.2, 1.0, "Short recording.", 0.8, [])]];
        var killed = false;
        _host.Workers.Script = async (job, context, cancel) =>
        {
            if (job.Kind == WorkerJobKinds.Transcribe && !killed)
            {
                killed = true;
                context.Send(new WorkerReply { Type = WorkerMessageTypes.Track, Track = new WorkerTrackInfo("mic", 2, false, 0.1, [[0.2, 1.0]], 1) });
                context.Send(new WorkerReply { Type = WorkerMessageTypes.Device, Device = Gpu });
                // Within the window: the engine's own percentage, no segments yet; then the process dies.
                context.Send(new WorkerReply { Type = WorkerMessageTypes.Progress, Percent = 40, TrackId = "mic" });
                await Task.Delay(20, CancellationToken.None);
                return unchecked((int)0xC0000409);
            }

            return await DefaultScript(job, context, cancel);
        };

        var id = await _host.RecordAsync("Short", 2, Mic);
        await IdleAsync();
        Assert.Equal(StageStates.Failed, Stage(await ManifestAsync(id), StageNames.Transcript).State);
        Assert.Null(await _host.Transcripts.LoadAsync(id, CancellationToken.None));
        // The engine's percentage within the window reached the stage.
        await _host.Sink.WaitForAsync("processing.progress", p => p.GetProperty("stages").EnumerateArray().Any(s => s.GetProperty("stage").GetString() == "transcript" && s.GetProperty("percent").ValueKind == JsonValueKind.Number && s.GetProperty("percent").GetInt32() == 40));

        await _host.ResultAsync("processing.retry", JsonSerializer.Serialize(new { recordingId = id, stage = "transcript", remedyId = "cpu" }));
        await IdleAsync();

        Assert.Equal(0, Jobs(WorkerJobKinds.Transcribe)[^1].Transcribe!.Tracks.Single().StartWindow);
        Assert.Equal(["Short recording."], (await TranscriptAsync(id)).Segments.Select(s => s.Text));
    }

    /// <summary>Speakers on two tracks: the worker reports each finished track, then (optionally) stops after the first.</summary>
    private Func<WorkerJob, ScriptedWorkerContext, CancellationToken, Task<int>> TwoTrackDiarizer(Func<int, CancellationToken, Task<bool>> afterTrack) => async (job, context, cancel) =>
    {
        if (job.Kind != WorkerJobKinds.Diarize)
        {
            return await DefaultScript(job, context, cancel);
        }

        lock (_jobs)
        {
            _jobs.Add(job);
        }

        var finished = new List<DiarizedTrack>();
        foreach (var track in job.Diarize!.Tracks)
        {
            cancel.ThrowIfCancellationRequested();
            var axis = track.Id == "mic" ? 0 : 1;
            var done = new DiarizedTrack(track.Id, [new SpeakerTurn(0, 1.1, 0, 0.7), new SpeakerTurn(1.1, 2, 1, 0.65)], [new SpeakerVoice(0, [1f, 0f, 0.1f * axis], 1), new SpeakerVoice(1, [0f, 1f, 0.1f * axis], 1)], 2);
            context.Send(new WorkerReply { Type = WorkerMessageTypes.Diarized, TrackId = track.Id, Diarized = done });
            finished.Add(done);
            if (!await afterTrack(finished.Count, cancel))
            {
                return unchecked((int)0xC0000409);
            }
        }

        context.Send(new WorkerReply { Type = WorkerMessageTypes.Result, Diarization = new DiarizeResult(finished, 4, 100) });
        return 0;
    };

    private void SpeechOnBothTracks() => Windows["system"] =
    [
        [new WorkerSegment(0.3, 1.0, "Can everyone hear me?", 0.9, []), new WorkerSegment(1.3, 1.9, "Good, then let us start.", 0.9, [])],
    ];

    [Fact]
    public async Task AStoppedSpeakerPassContinuesWithTheTracksItHadNotFinished()
    {
        InstallAll();
        SpeechOnBothTracks();
        var stopAfterFirst = true;
        _host.Workers.Script = TwoTrackDiarizer((n, _) => Task.FromResult(!(stopAfterFirst && n == 1)));

        var id = await RecordAndProcessAsync();

        var manifest = await ManifestAsync(id);
        Assert.Equal(StageStates.Failed, Stage(manifest, StageNames.Speakers).State);
        var failure = Assert.Single(manifest.Failures);
        Assert.Equal("The transcript is kept without speakers; 1 track already done is kept, and trying again continues with the others.", failure.Kept);
        var partial = await _host.Transcripts.LoadSpeakersPartialAsync(id, CancellationToken.None);
        Assert.Equal(["mic"], partial!.Tracks.Select(t => t.TrackId));

        stopAfterFirst = false;
        await _host.ResultAsync("processing.retry", JsonSerializer.Serialize(new { recordingId = id, stage = "speakers", remedyId = "retry" }));
        await IdleAsync();

        var jobs = Jobs(WorkerJobKinds.Diarize);
        Assert.Equal(["mic", "system"], jobs[0].Diarize!.Tracks.Select(t => t.Id));
        Assert.Equal(["system"], jobs[^1].Diarize!.Tracks.Select(t => t.Id));
        var transcript = await TranscriptAsync(id);
        Assert.All(transcript.Segments, s => Assert.NotNull(s.Speaker));
        Assert.Equal(4, transcript.Speakers.Count);
        Assert.Null(await _host.Transcripts.LoadSpeakersPartialAsync(id, CancellationToken.None));
        Assert.False(File.Exists(Path.Combine(_host.Store.GetProjectFolder(id), ProjectLayout.SpeakersPartialFile)));
        var history = await _host.Store.ReadHistoryAsync(id, CancellationToken.None);
        Assert.Contains(history, h => h.Stage == "speakers" && h.Event == "started" && h.Summary == "Identifying speakers (continuing where it stopped)");
    }

    [Fact]
    public async Task ABusyPauseDuringSpeakersResumesAfterTheFinishedTrack()
    {
        InstallAll();
        SpeechOnBothTracks();
        var paused = new TaskCompletionSource();
        _host.Workers.Script = TwoTrackDiarizer(async (n, cancel) =>
        {
            if (n == 1 && !paused.Task.IsCompleted)
            {
                // Busy with the second track when the PC gets busy.
                paused.TrySetResult();
                await Task.Delay(TimeSpan.FromSeconds(30), cancel);
            }

            return true;
        });

        var id = await _host.RecordAsync("Busy speakers", 2, Mic, SystemAudio);
        await paused.Task.WaitAsync(TimeSpan.FromSeconds(20));
        _host.Gate.SetManual(true);
        await WaitUntilAsync(async () => Stage(await ManifestAsync(id), StageNames.Speakers).Label == "Paused · Paused by you", "speakers to pause");
        _host.Gate.SetManual(false);
        await IdleAsync();

        var jobs = Jobs(WorkerJobKinds.Diarize);
        Assert.Equal(["system"], jobs[^1].Diarize!.Tracks.Select(t => t.Id));
        Assert.Equal(StageStates.Done, Stage(await ManifestAsync(id), StageNames.Speakers).State);
        Assert.All((await TranscriptAsync(id)).Segments, s => Assert.NotNull(s.Speaker));
    }

    [Fact]
    public async Task WithAnExpectedCountVoicesOnSeveralTracksAreGroupedToThatCount()
    {
        InstallAll();
        SpeechOnBothTracks();
        _host.Workers.Script = TwoTrackDiarizer((_, _) => Task.FromResult(true));
        await _host.ResultAsync("settings.set", """{"speakers":{"expectedSpeakers":2}}""");

        var id = await RecordAndProcessAsync();

        // Per track the count is not applied (it says nothing about each track); across them, voices are grouped.
        Assert.Equal(-1, Jobs(WorkerJobKinds.Diarize)[^1].Diarize!.NumClusters);
        var transcript = await TranscriptAsync(id);
        Assert.Equal(2, transcript.Speakers.Count);
        var history = await _host.Store.ReadHistoryAsync(id, CancellationToken.None);
        var found = Assert.Single(history, h => h.Stage == "speakers" && h.Event == "completed");
        Assert.Equal("Found 2 speakers", found.Summary);
        Assert.Contains("grouped by sound into the 2 expected speakers", found.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IdentifyingSpeakersAgainWhileTheyRunStartsOverWithTheNewSettings()
    {
        InstallAll();
        var firstStarted = new TaskCompletionSource();
        _host.Workers.Script = async (job, context, cancel) =>
        {
            if (job.Kind == WorkerJobKinds.Diarize && !firstStarted.Task.IsCompleted)
            {
                lock (_jobs)
                {
                    _jobs.Add(job);
                }

                // The first speaker job is still busy when the expected count changes.
                firstStarted.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancel);
            }

            return await DefaultScript(job, context, cancel);
        };

        var id = await _host.RecordAsync("Again", 2, Mic, SystemAudio);
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(20));
        await _host.ResultAsync("settings.set", """{"speakers":{"expectedSpeakers":3}}""");
        await _host.ResultAsync("processing.retry", JsonSerializer.Serialize(new { recordingId = id, stage = "speakers" }));
        await IdleAsync();

        var jobs = Jobs(WorkerJobKinds.Diarize);
        Assert.Equal([-1, 3], jobs.Select(j => j.Diarize!.NumClusters));
        Assert.Equal(StageStates.Done, Stage(await ManifestAsync(id), StageNames.Speakers).State);
        Assert.Empty((await ManifestAsync(id)).Failures);
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
