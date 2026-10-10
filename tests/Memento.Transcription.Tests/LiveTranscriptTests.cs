using System.Text.Json;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Processing;
using Memento.Core.Settings;
using Memento.Core.Tests.Fakes;
using Memento.Core.Workers;
using Memento.Transcription.Live;
using Microsoft.Extensions.DependencyInjection;
using static Memento.Core.Tests.Fakes.TestRecordings;

namespace Memento.Transcription.Tests;

/// <summary>
/// The live transcript while recording (2.0) with the simulated engine in manual mode and a scripted worker: off by
/// default, windows of the mix heard in order with their lines appended and never saved, the Small model on the
/// processor unless Settings allows the card, and yielding to a full pass and to any job that wants the card.
/// </summary>
public sealed class LiveTranscriptTests : IDisposable
{
    private static readonly WorkerDevice Cpu = new("cpu", "CPU", null, null, "1.9.1");
    private static readonly WorkerDevice Gpu = new("vulkan", "GPU (Vulkan)", "NVIDIA GeForce RTX 3060 Laptop GPU", 0, "1.9.1");

    private readonly BridgeTestHost _host;
    private readonly List<WorkerJob> _jobs = [];
    private readonly List<LiveAudio> _heard = [];
    private TaskCompletionSource? _heavyRelease;

    public LiveTranscriptTests()
    {
        _host = new BridgeTestHost(configure: services =>
        {
            services.AddSingleton(TestCatalogs.Tiny);
            services.AddSingleton<LiveTranscriptService>();
            services.AddSingleton<IProcessingStage>(sp => new FakeStage(Memento.Core.Projects.StageNames.Transcript, 10, heavy: true, sp.GetRequiredService<StageStatusWriter>())
            {
                Behaviour = async (_, cancel) => await (_heavyRelease?.Task ?? Task.CompletedTask).WaitAsync(cancel),
            });
        });
        _host.Probe.Snapshot = FakeResourceProbe.WithGpu(5L << 30);
        _host.Workers.Script = ScriptAsync;
    }

    private LiveTranscriptService Live => _host.Get<LiveTranscriptService>();

    public void Dispose()
    {
        Live.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _host.Dispose();
    }

    [Fact]
    public async Task OffByDefaultNothingIsHeardAndNoWorkerStarts()
    {
        Assert.Equal(TranscriptionSettings.TimingAfter, new TranscriptionSettings().Timing);
        Assert.False(new TranscriptionSettings().LiveOnGpu);
        TestCatalogs.Install(_host, "whisper-small");
        await _host.StartAsync("Quiet", Mic);
        await RecordAsync(25);

        await Live.TickAsync(CancellationToken.None);
        await Live.TickAsync(CancellationToken.None);

        Assert.Empty(_host.Workers.Started);
        Assert.Empty(_host.Sink.Payloads(BridgeEventNames.RecordingLiveTranscript));
    }

    [Fact]
    public async Task WindowsOfTheMixAreHeardInOrderAndTheirLinesAppendedAsAProvisionalDraft()
    {
        TestCatalogs.Install(_host, "whisper-large-v3-turbo", "whisper-small");
        await LiveOnAsync();
        var (sessionId, recordingId) = await _host.StartAsync("Live", Mic, SystemAudio);
        await RecordAsync(15);
        await TickUntilAsync(() => _heard.Count >= 1, "the first window heard");
        await RecordAsync(10);

        await TickUntilAsync(() => _heard.Count >= 2, "two windows heard");

        var job = Assert.Single(_jobs).Live!;
        Assert.Equal(LiveTranscriptService.SmallModelId, job.ModelId); // never Turbo, though it is installed
        Assert.Equal([WorkerRuntimes.Cpu], job.Runtimes);
        Assert.InRange(job.Threads, 1, 4);
        Assert.Equal([0, 1], _heard.Select(a => a.Window));
        Assert.Equal([0.0, 10.0], _heard.Select(a => a.StartSeconds));
        Assert.All(_heard, a => Assert.Equal(LiveAudio.SampleRate * 10, a.Decode().Length));

        var last = _host.Sink.Payloads(BridgeEventNames.RecordingLiveTranscript).Last();
        Assert.Equal("sessionId,segments,state,engine,note", string.Join(",", last.EnumerateObject().Select(p => p.Name)));
        Assert.Equal(sessionId, last.GetProperty("sessionId").GetString());
        Assert.Equal("listening", last.GetProperty("state").GetString());
        Assert.Equal("Local · CPU · Small", last.GetProperty("engine").GetString());
        Assert.Equal(
            """[{"start":1,"end":3,"text":"Line 0."},{"start":11,"end":13,"text":"Line 1."}]""",
            last.GetProperty("segments").GetRawText());

        // Never saved: nothing of the draft is in the project folder after the session (files are not read while it
        // records: a reader could hold a file the coordinator is replacing).
        var folder = _host.Store.GetProjectFolder(recordingId);
        await _host.ResultAsync("recording.stop", JsonSerializer.Serialize(new { sessionId }));
        await _host.Recordings.WhenIdleAsync();
        await _host.Processing.WhenIdleAsync().WaitAsync(Patience.Ceiling);
        await Live.TickAsync(CancellationToken.None);
        Assert.Empty(Live.Lines);
        Assert.False(File.Exists(Path.Combine(folder, "transcript.json")));
        Assert.DoesNotContain(Directory.EnumerateFiles(folder, "*.json*", SearchOption.AllDirectories), f => File.ReadAllText(f).Contains("Line 0.", StringComparison.Ordinal));
        await WaitUntilAsync(() => _host.Workers.Started.All(p => p.Exited.IsCompleted), "the live worker to end");
    }

    [Fact]
    public async Task ADraftNeverQueuesUpWorkItSkipsToTheLatestCompleteWindow()
    {
        TestCatalogs.Install(_host, "whisper-small");
        await LiveOnAsync();
        await _host.StartAsync("Behind", Mic);
        await RecordAsync(45);

        await TickUntilAsync(() => _heard.Count >= 1, "a window heard");

        Assert.Equal([3], _heard.Select(a => a.Window));
    }

    [Fact]
    public async Task LiveYieldsToAFullPassAndCarriesOnAfterIt()
    {
        TestCatalogs.Install(_host, "whisper-small");
        await _host.Settings.UpdateAsync(s => s with { Transcription = s.Transcription with { PauseWhenBusy = false } }, CancellationToken.None);
        _heavyRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // An earlier recording's full pass is running while the next one records.
        await _host.RecordAsync("Earlier", 3, Mic);
        await WaitUntilAsync(() => _host.Get<ProcessingOrchestrator>().IsHeavyStageRunning, "the full pass to run");
        await LiveOnAsync();
        await _host.StartAsync("Now", Mic);
        await RecordAsync(15);

        await Live.TickAsync(CancellationToken.None);
        await Live.TickAsync(CancellationToken.None);

        Assert.Empty(_heard);
        Assert.DoesNotContain(_jobs, j => j.Kind == WorkerJobKinds.Live);
        var paused = _host.Sink.Payloads(BridgeEventNames.RecordingLiveTranscript).Last();
        Assert.Equal("paused", paused.GetProperty("state").GetString());
        Assert.Contains("never slows a full transcript", paused.GetProperty("note").GetString(), StringComparison.Ordinal);

        _heavyRelease.SetResult();
        await _host.Processing.WhenIdleAsync().WaitAsync(Patience.Ceiling);
        await TickUntilAsync(() => _heard.Count >= 1, "a window heard after the full pass");
        Assert.Equal("listening", _host.Sink.Payloads(BridgeEventNames.RecordingLiveTranscript).Last().GetProperty("state").GetString());
    }

    [Fact]
    public async Task OnTheCardOnlyWhenAllowedAndItLetsGoAtOnceForAnotherJob()
    {
        TestCatalogs.Install(_host, "whisper-small");
        await LiveOnAsync(onGpu: true);
        await _host.StartAsync("Card", Mic);
        await RecordAsync(15);
        await TickUntilAsync(() => _heard.Count >= 1, "a window heard on the card");
        Assert.Equal([TranscriptionDefaults.RuntimeVulkan, WorkerRuntimes.Cpu], _jobs[0].Live!.Runtimes);
        Assert.True(Live.OnGpu);

        // A full pass or a document asks for the card: it gets it, and the live draft goes on from the processor.
        var workers = _host.Get<WorkerClient>();
        var other = new WorkerJob(WorkerJobKinds.Transcribe, Transcribe: new TranscribeJob([], "m", "whisper-large-v3-turbo", [WorkerRuntimes.Vulkan, WorkerRuntimes.Cpu], -1, null, "auto", "p", 4, true, 600, 0));
        var full = await workers.RunAsync(other, null, CancellationToken.None).WaitAsync(Patience.Ceiling);
        Assert.Equal(WorkerMessageTypes.Result, full.Type);
        Assert.True(_host.Workers.Started[0].Exited.IsCompleted);

        _host.Session.Advance(TimeSpan.FromSeconds(10));
        await _host.Session.CheckpointAsync(CancellationToken.None);
        await TickUntilAsync(() => _heard.Count >= 2, "a window heard on the processor");
        var again = _jobs.Last(j => j.Kind == WorkerJobKinds.Live).Live!;
        Assert.Equal([WorkerRuntimes.Cpu], again.Runtimes);
        Assert.Equal("Local · CPU · Small", _host.Sink.Payloads(BridgeEventNames.RecordingLiveTranscript).Last().GetProperty("engine").GetString());
    }

    [Fact]
    public async Task NeverWaitsForTheCardWhileAnotherJobHoldsIt()
    {
        TestCatalogs.Install(_host, "whisper-small");
        await LiveOnAsync(onGpu: true);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _holdCard = release.Task;
        var workers = _host.Get<WorkerClient>();
        var other = new WorkerJob(WorkerJobKinds.Transcribe, Transcribe: new TranscribeJob([], "m", "whisper-large-v3-turbo", [WorkerRuntimes.Vulkan, WorkerRuntimes.Cpu], -1, null, "auto", "p", 4, true, 600, 0));
        var holder = workers.RunAsync(other, null, CancellationToken.None);
        await WaitUntilAsync(() => workers.IsGpuBusy, "the other job to hold the card");

        await _host.StartAsync("Card busy", Mic);
        await RecordAsync(15);
        await TickUntilAsync(() => _heard.Count >= 1, "a window heard on the processor");

        Assert.Equal([WorkerRuntimes.Cpu], _jobs.Single(j => j.Kind == WorkerJobKinds.Live).Live!.Runtimes);
        release.SetResult();
        await holder.WaitAsync(Patience.Ceiling);
    }

    [Fact]
    public async Task WithoutTheSmallOrBaseModelTheCardSaysWhatToInstall()
    {
        TestCatalogs.Install(_host, "whisper-large-v3-turbo");
        await LiveOnAsync();
        await _host.StartAsync("No model", Mic);
        await RecordAsync(12);

        await Live.TickAsync(CancellationToken.None);

        Assert.DoesNotContain(_jobs, j => j.Kind == WorkerJobKinds.Live);
        var last = _host.Sink.Payloads(BridgeEventNames.RecordingLiveTranscript).Last();
        Assert.Equal("unavailable", last.GetProperty("state").GetString());
        Assert.Equal(
            "The live transcript needs the Small or Base transcription model. Install one in Settings › Transcription; the full transcript is still made after you stop.",
            last.GetProperty("note").GetString());
    }

    [Fact]
    public async Task APausedRecordingPausesTheDraft()
    {
        TestCatalogs.Install(_host, "whisper-small");
        await LiveOnAsync();
        var (sessionId, _) = await _host.StartAsync("Paused", Mic);
        await RecordAsync(5);
        await _host.ResultAsync("recording.pause", JsonSerializer.Serialize(new { sessionId }));

        await Live.TickAsync(CancellationToken.None);

        var last = _host.Sink.Payloads(BridgeEventNames.RecordingLiveTranscript).Last();
        Assert.Equal("paused", last.GetProperty("state").GetString());
        Assert.Equal("Paused with the recording. Words carry on when you resume.", last.GetProperty("note").GetString());
    }

    private Task? _holdCard;

    private async Task LiveOnAsync(bool onGpu = false) =>
        await _host.Settings.UpdateAsync(
            s => s with { Transcription = s.Transcription with { Timing = TranscriptionSettings.TimingDuring, LiveOnGpu = onGpu } },
            CancellationToken.None);

    private async Task RecordAsync(double seconds)
    {
        _host.Session.Advance(TimeSpan.FromSeconds(seconds));
        await _host.Session.CheckpointAsync(CancellationToken.None);
    }

    private async Task TickUntilAsync(Func<bool> done, string what)
    {
        var deadline = Environment.TickCount64 + Patience.CeilingMs;
        while (!done())
        {
            if (Environment.TickCount64 > deadline)
            {
                throw new TimeoutException($"Timed out waiting for {what}.");
            }

            await Live.TickAsync(CancellationToken.None);
            await Task.Delay(10);
        }
    }

    private async Task<int> ScriptAsync(WorkerJob job, ScriptedWorkerContext context, CancellationToken cancel)
    {
        lock (_jobs)
        {
            _jobs.Add(job);
        }

        context.Send(new WorkerReply { Type = WorkerMessageTypes.Ready, Pid = 1 });
        if (job.Kind != WorkerJobKinds.Live)
        {
            if (_holdCard is { } hold)
            {
                await hold.WaitAsync(cancel);
            }

            context.Send(new WorkerReply { Type = WorkerMessageTypes.Result });
            return 0;
        }

        context.Send(new WorkerReply { Type = WorkerMessageTypes.Device, Device = job.Live!.Runtimes.Contains(WorkerRuntimes.Vulkan) ? Gpu : Cpu });
        await foreach (var command in context.Commands.ReadAllAsync(cancel))
        {
            if (command.Type == WorkerMessageTypes.End)
            {
                break;
            }

            if (command.Audio is { } audio)
            {
                lock (_heard)
                {
                    _heard.Add(audio);
                }

                context.Send(new WorkerReply
                {
                    Type = WorkerMessageTypes.Heard,
                    Window = audio.Window,
                    Segments = [new WorkerSegment(audio.StartSeconds + 1, audio.StartSeconds + 3, $"Line {audio.Window}.", 0.9, [])],
                });
            }
        }

        context.Send(new WorkerReply { Type = WorkerMessageTypes.Result });
        return 0;
    }
}
