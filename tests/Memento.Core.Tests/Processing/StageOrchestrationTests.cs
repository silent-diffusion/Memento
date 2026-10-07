using System.Text.Json;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Processing;
using Memento.Core.Projects;
using Memento.Core.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using static Memento.Core.Tests.Fakes.TestRecordings;

namespace Memento.Core.Tests.Processing;

/// <summary>The orchestrator with registered stages: order, failures and remedies, cancel, pause and resume.</summary>
public sealed class StageOrchestrationTests : IDisposable
{
    private readonly List<string> _log = [];
    private readonly BridgeTestHost _host;
    private FakeStage? _transcript;
    private FakeStage? _speakers;

    public StageOrchestrationTests()
    {
        _host = new BridgeTestHost(configure: services =>
        {
            services.AddSingleton<IProcessingStage>(sp => _transcript = new FakeStage(StageNames.Transcript, 10, true, sp.GetRequiredService<StageStatusWriter>()) { Log = _log });
            services.AddSingleton<IProcessingStage>(sp => _speakers = new FakeStage(StageNames.Speakers, 20, true, sp.GetRequiredService<StageStatusWriter>()) { Log = _log });
        });
        _ = _host.Processing;
    }

    public void Dispose() => _host.Dispose();

    private FakeStage Transcript => _transcript!;

    private FakeStage Speakers => _speakers!;

    private async Task<ProjectManifest> ManifestAsync(string id) => await _host.Store.LoadAsync(id, CancellationToken.None);

    private static StageStatus Stage(ProjectManifest manifest, string name) => Assert.Single(manifest.Stages, s => s.Stage == name);

    [Fact]
    public async Task AStoredRecordingRunsTranscriptThenSpeakersInPipelineOrder()
    {
        var id = await _host.RecordAsync("Ordered", 1, Mic);
        await _host.Processing.WhenIdleAsync();

        Assert.Equal([StageNames.Transcript, StageNames.Speakers], _log);
        var manifest = await ManifestAsync(id);
        Assert.Equal([StageNames.Stored, StageNames.Transcript, StageNames.Speakers], manifest.Stages.Select(s => s.Stage));
        Assert.All(manifest.Stages, s => Assert.Equal(StageStates.Done, s.State));
        var row = Assert.Single((await _host.ResultAsync("library.list")).GetProperty("recordings").EnumerateArray());
        Assert.False(row.GetProperty("isProcessing").GetBoolean());
    }

    [Fact]
    public async Task ADisabledStageIsNotQueued()
    {
        _ = _host.Processing;
        Speakers.Enabled = false;

        var id = await _host.RecordAsync("Only transcript", 1, Mic);
        await _host.Processing.WhenIdleAsync();

        Assert.Equal([StageNames.Transcript], _log);
        Assert.DoesNotContain((await ManifestAsync(id)).Stages, s => s.Stage == StageNames.Speakers);
    }

    [Fact]
    public async Task AFailureIsKeptWithRemediesAndRetryAppliesTheRemedy()
    {
        Transcript.Behaviour = (run, _) => Transcript.FailAsync(run, "The engine stopped.", new Remedy(Remedies.Cpu, "Retry on CPU"), new Remedy("model:small", "Use the Small model"));
        var id = await RecordAsync();

        var got = await _host.ResultAsync("transcript.get", JsonSerializer.Serialize(new { recordingId = id }));
        Assert.Equal("failed", got.GetProperty("status").GetString());
        var failure = got.GetProperty("failure");
        Assert.Equal("transcript", failure.GetProperty("stage").GetString());
        Assert.Equal("The engine stopped.", failure.GetProperty("message").GetString());
        Assert.Equal(["cpu", "model:small"], failure.GetProperty("remedies").EnumerateArray().Select(r => r.GetProperty("id").GetString()));

        Transcript.Behaviour = null;
        await _host.ResultAsync("processing.retry", JsonSerializer.Serialize(new { recordingId = id, stage = "transcript", remedyId = "cpu" }));
        await WaitIdleAsync();

        var manifest = await ManifestAsync(id);
        Assert.Equal(StageStates.Done, Stage(manifest, StageNames.Transcript).State);
        Assert.Equal(StageStates.Done, Stage(manifest, StageNames.Speakers).State);
        Assert.True(manifest.Processing!.ForceCpu);
        Assert.Empty(manifest.Failures);

        await _host.ResultAsync("processing.retry", JsonSerializer.Serialize(new { recordingId = id, stage = "transcript", remedyId = "model:medium" }));
        await WaitIdleAsync();
        Assert.Equal("medium", (await ManifestAsync(id)).Processing!.ModelId);
    }

    [Fact]
    public async Task RetryRefusesUnknownRemediesStagesAndModels()
    {
        var id = await RecordAsync();

        async Task<string> Code(object parameters) =>
            (await _host.CallAsync("processing.retry", JsonSerializer.Serialize(parameters))).GetProperty("error").GetProperty("code").GetString()!;

        Assert.Equal("bridge.invalidParams", await Code(new { recordingId = id, stage = "transcript", remedyId = "reboot" }));
        Assert.Equal("bridge.invalidParams", await Code(new { recordingId = id, stage = "minutes" }));
        Assert.Equal("models.notFound", await Code(new { recordingId = id, stage = "transcript", remedyId = "model:nope" }));
        Assert.Equal("project.notFound", await Code(new { recordingId = "20990101-000000-zzzzzz", stage = "transcript" }));
    }

    [Fact]
    public async Task CancelStopsTheRunningStageAndRecordsItAsCancelled()
    {
        var started = new TaskCompletionSource();
        Transcript.Behaviour = async (_, token) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
        };
        var id = await _host.RecordAsync("Cancel me", 1, Mic);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await _host.ResultAsync("processing.cancel", JsonSerializer.Serialize(new { recordingId = id, stage = "transcript" }));
        await WaitIdleAsync();

        var manifest = await ManifestAsync(id);
        Assert.Equal(StageStates.Failed, Stage(manifest, StageNames.Transcript).State);
        var failure = Assert.Single(manifest.Failures);
        Assert.Equal(ProjectStageFailure.CauseCancelled, failure.Cause);
        Assert.Equal("retry", failure.Remedies[0].Id);
        var history = await _host.Store.ReadHistoryAsync(id, CancellationToken.None);
        Assert.Contains(history, h => h.Stage == "transcript" && h.Summary == "Cancelled");
    }

    [Fact]
    public async Task PausingHoldsHeavyStagesAndResumeRunsThem()
    {
        await _host.ResultAsync("processing.pause");
        var id = await _host.RecordAsync("Paused", 1, Mic);
        await TestRecordings.WaitUntilAsync(
            async () => (await ManifestAsync(id)).Stages.Any(s => s.Stage == StageNames.Transcript && s.Label == "Paused · Paused by you"),
            "the transcript to wait");

        Assert.Empty(_log);
        var got = await _host.ResultAsync("transcript.get", JsonSerializer.Serialize(new { recordingId = id }));
        Assert.Equal("paused", got.GetProperty("status").GetString());
        Assert.Equal("Paused by you", (await _host.ResultAsync("status.get")).GetProperty("processingPaused").GetString());

        await _host.ResultAsync("processing.resume");
        await WaitIdleAsync();

        Assert.Equal([StageNames.Transcript, StageNames.Speakers], _log);
    }

    [Fact]
    public async Task ARunningHeavyStageIsInterruptedByAPauseAndRunsAgainAfterwards()
    {
        var runs = 0;
        var first = new TaskCompletionSource();
        Transcript.Behaviour = async (run, token) =>
        {
            if (Interlocked.Increment(ref runs) == 1)
            {
                first.TrySetResult();
                await Task.Delay(Timeout.Infinite, token);
            }

            await _host.Get<StageStatusWriter>().SetAsync(run.RecordingId, new StageStatus(StageNames.Transcript, StageStates.Done, null, "Done"), token);
        };
        var id = await _host.RecordAsync("Interrupted", 1, Mic);
        await first.Task.WaitAsync(TimeSpan.FromSeconds(10));

        _host.Gate.SetManual(true);
        await TestRecordings.WaitUntilAsync(
            async () => (await ManifestAsync(id)).Stages.Any(s => s.Stage == StageNames.Transcript && s.State == StageStates.Queued),
            "the stage to go back to the queue");
        _host.Gate.SetManual(false);
        await WaitIdleAsync();

        Assert.Equal(2, runs);
        Assert.Equal(StageStates.Done, Stage(await ManifestAsync(id), StageNames.Transcript).State);
    }

    [Fact]
    public async Task ShutdownLeavesTheStageQueuedForTheNextLaunch()
    {
        var started = new TaskCompletionSource();
        Transcript.Behaviour = async (_, token) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
        };
        var id = await _host.RecordAsync("Closing", 1, Mic);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await _host.Processing.StopAsync();

        Assert.Equal(StageStates.Queued, Stage(await ManifestAsync(id), StageNames.Transcript).State);
    }

    [Fact]
    public void RemediesMapToTheProcessingRequest()
    {
        var request = new ProcessingRequest();

        Assert.True(ProcessingOrchestrator.ApplyRemedy(request, "cpu").ForceCpu);
        Assert.Equal("small", ProcessingOrchestrator.ApplyRemedy(request, "model:small").ModelId);
        Assert.Same(request, ProcessingOrchestrator.ApplyRemedy(request, "retry"));
        Assert.Same(request, ProcessingOrchestrator.ApplyRemedy(request, null));
    }

    private async Task<string> RecordAsync()
    {
        var id = await _host.RecordAsync("Recording", 1, Mic);
        await WaitIdleAsync();
        return id;
    }

    private async Task WaitIdleAsync()
    {
        await Task.Delay(50);
        await _host.Processing.WhenIdleAsync();
    }
}
