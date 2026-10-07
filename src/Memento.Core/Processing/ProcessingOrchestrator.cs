using System.Diagnostics;
using System.Threading.Channels;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Engines;
using Memento.Core.Host;
using Memento.Core.Library;
using Memento.Core.Models;
using Memento.Core.Projects;
using Memento.Core.Settings;
using Memento.Core.Status;
using Memento.Core.Workers;
using Microsoft.Extensions.Logging;

namespace Memento.Core.Processing;

/// <summary>
/// Runs the processing stages that follow <c>stored</c>, one recording at a time on a background worker
/// (ARCHITECTURE.md §6): <c>transcript</c>, then <c>speakers</c>, then <c>optimize</c> last, each when registered and
/// enabled. Queued work is written into the manifest, so a stage interrupted by closing Memento resumes at the next
/// launch (<see cref="ResumePendingAsync"/>). Heavy stages wait while the <see cref="ProcessingGate"/> is closed and are
/// interrupted (keeping their partial results) when it closes; they resume by themselves when it opens again.
/// </summary>
public sealed partial class ProcessingOrchestrator : IAsyncDisposable, IDisposable
{
    public static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(1);

    private readonly IReadOnlyList<IProcessingStage> _stages;
    private readonly ILibraryIndex _index;
    private readonly IProjectStore _store;
    private readonly ISettingsStore _settings;
    private readonly StageStatusWriter _status;
    private readonly ProcessingGate _gate;
    private readonly IResourceProbe _probe;
    private readonly IFreeSpaceProbe _freeSpace;
    private readonly ILibraryLocation _library;
    private readonly RecordingStatusBoard _board;
    private readonly FooterStatusService _footer;
    private readonly WorkerClient? _workers;
    private readonly IModelManager? _models;
    private readonly TimeProvider _time;
    private readonly ILogger<ProcessingOrchestrator> _logger;
    private readonly Channel<string> _queue = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _sync = new();
    private readonly HashSet<string> _pending = new(StringComparer.Ordinal);
    private readonly Task _worker;
    private readonly Task _sampler;
    private TaskCompletionSource _idle = NewCompleted();
    private Running? _running;

    public ProcessingOrchestrator(
        IEnumerable<IProcessingStage> stages,
        ILibraryIndex index,
        IProjectStore store,
        ISettingsStore settings,
        StageStatusWriter status,
        ProcessingGate gate,
        IResourceProbe probe,
        IFreeSpaceProbe freeSpace,
        ILibraryLocation library,
        RecordingStatusBoard board,
        FooterStatusService footer,
        TimeProvider time,
        ILogger<ProcessingOrchestrator> logger,
        WorkerClient? workers = null,
        IModelManager? models = null)
    {
        _stages = stages.OrderBy(s => s.Order).ToList();
        _index = index;
        _store = store;
        _settings = settings;
        _status = status;
        _gate = gate;
        _probe = probe;
        _freeSpace = freeSpace;
        _library = library;
        _board = board;
        _footer = footer;
        _workers = workers;
        _models = models;
        _time = time;
        _logger = logger;
        _gate.Changed += OnGateChanged;
        if (_models is not null)
        {
            _models.Installed += OnModelInstalled;
        }

        _worker = Task.Run(RunAsync);
        _sampler = Task.Run(SampleAsync);
    }

    /// <summary>The registered stages in pipeline order.</summary>
    public IReadOnlyList<IProcessingStage> Stages => _stages;

    /// <summary>The recording has stage work queued or running, so it must not be deleted underneath it.</summary>
    public bool IsBusy(string recordingId)
    {
        lock (_sync)
        {
            return _pending.Contains(recordingId) || _running?.RecordingId == recordingId;
        }
    }

    /// <summary>The stage running now for <paramref name="recordingId"/>, or <c>null</c>.</summary>
    public string? RunningStage(string recordingId)
    {
        lock (_sync)
        {
            return _running is { } running && running.RecordingId == recordingId ? running.Stage : null;
        }
    }

    /// <summary>
    /// Called once <c>stored</c> is done: queues every registered stage that applies with the current settings and marks
    /// them queued in the manifest (transcript and speakers when enabled, optimize for AAC or MP3).
    /// </summary>
    public async Task EnqueueAfterStoredAsync(string recordingId, CancellationToken cancellationToken)
    {
        var settings = _settings.Current;
        var applicable = _stages.Where(s => s.AppliesTo(settings)).ToList();
        if (applicable.Count == 0)
        {
            return;
        }

        var manifest = await _store.LoadAsync(recordingId, cancellationToken);
        foreach (var stage in applicable)
        {
            // A stage already done (a recovered recording queued again) stays done.
            if (StageList.Find(manifest.Stages, stage.Name) is { State: StageStates.Done })
            {
                continue;
            }

            manifest = await _status.SetAsync(recordingId, StageStatusWriter.QueuedStatus(stage.Name), cancellationToken);
        }

        Schedule(recordingId);
    }

    /// <summary>Launch: queues every recording whose stage work was queued or running when Memento closed.</summary>
    public async Task<int> ResumePendingAsync(CancellationToken cancellationToken)
    {
        var names = _stages.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
        var count = 0;
        foreach (var entry in await _index.ListProcessingAsync(cancellationToken))
        {
            if (entry.Stages.Any(s => names.Contains(s.Stage) && StageStates.IsRunning(s.State)))
            {
                Schedule(entry.Summary.Id);
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// <c>processing.retry</c>: queues a failed stage again, applying a remedy (<c>cpu</c>, <c>model:&lt;id&gt;</c>,
    /// <c>retry</c>). Retrying the transcript also queues speakers (when enabled) and topics.
    /// </summary>
    /// <exception cref="BridgeException"><c>bridge.invalidParams</c> for an unknown stage or remedy, <c>models.notFound</c>.</exception>
    public async Task RetryAsync(string recordingId, string stage, string? remedyId, CancellationToken cancellationToken)
    {
        var registered = _stages.FirstOrDefault(s => s.Name == stage)
            ?? throw new BridgeException(BridgeErrorCodes.InvalidParams, $"There is no '{stage}' stage to retry in this version.");
        var manifest = await _store.LoadAsync(recordingId, cancellationToken);
        var current = StageList.Find(manifest.Stages, stage);
        if (RunningStage(recordingId) == stage)
        {
            // Running now with the settings it started with (Identify speakers again after changing the expected
            // count): stop it and run it again from the start of the queue, with the settings as they are now.
            await CancelStageAsync(recordingId, stage, StageStopReason.Requeued);
            manifest = await _store.LoadAsync(recordingId, cancellationToken);
        }
        else if (current is { State: StageStates.Active or StageStates.Queued })
        {
            // Waiting its turn: it reads the settings when it starts.
            Schedule(recordingId);
            return;
        }

        var request = ApplyRemedy(manifest.Processing ?? new ProcessingRequest(), remedyId, _models?.Catalog);
        await _status.SetAsync(recordingId, StageStatusWriter.QueuedStatus(registered.Name), cancellationToken);
        if (!ReferenceEquals(request, manifest.Processing))
        {
            await _store.UpdateAsync(recordingId, m => m with { Processing = request }, cancellationToken);
        }

        if (stage == StageNames.Transcript)
        {
            await QueueDependentsAsync(recordingId, cancellationToken);
        }

        LogRetry(recordingId, stage, remedyId ?? "retry");
        Schedule(recordingId);
    }

    /// <summary>
    /// <c>transcript.retranscribe</c>: a new transcription pass with <paramref name="request"/>, followed by speakers
    /// when enabled. The current transcript stays until the new one replaces it (kept as a version when history is on).
    /// </summary>
    public async Task RetranscribeAsync(string recordingId, ProcessingRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_stages.All(s => s.Name != StageNames.Transcript))
        {
            throw new BridgeException(DomainErrorCodes.EngineUnavailable, "Transcription is not available in this build.", "The transcription worker is not installed.");
        }

        if (RunningStage(recordingId) == StageNames.Transcript)
        {
            await CancelStageAsync(recordingId, StageNames.Transcript, StageStopReason.Requeued);
        }

        await _store.UpdateAsync(recordingId, m => m with { Processing = request with { Retranscribe = true } }, cancellationToken);
        await _status.SetAsync(recordingId, StageStatusWriter.QueuedStatus(StageNames.Transcript), cancellationToken);
        await QueueDependentsAsync(recordingId, cancellationToken);
        Schedule(recordingId);
    }

    /// <summary>Stops <paramref name="stage"/> if it is running for the recording, so it can start over with new options.</summary>
    public Task StopForRequeueAsync(string recordingId, string stage) => CancelStageAsync(recordingId, stage, StageStopReason.Requeued);

    /// <summary>
    /// <c>processing.cancel</c>: stops the stage (it keeps what it made) and records it as failed with "cancelled", so
    /// <c>processing.retry</c> can run it again. A queued stage is taken out of the queue the same way.
    /// </summary>
    public async Task CancelAsync(string recordingId, string stage, CancellationToken cancellationToken)
    {
        if (_stages.All(s => s.Name != stage))
        {
            throw new BridgeException(BridgeErrorCodes.InvalidParams, $"There is no '{stage}' stage to cancel in this version.");
        }

        if (RunningStage(recordingId) == stage)
        {
            await CancelStageAsync(recordingId, stage, StageStopReason.Cancelled);
            return;
        }

        var manifest = await _store.LoadAsync(recordingId, cancellationToken);
        if (StageList.Find(manifest.Stages, stage) is { State: StageStates.Queued or StageStates.Active })
        {
            await RecordCancelledAsync(recordingId, stage, cancellationToken);
        }
    }

    /// <summary>Stops work for one recording (before it is deleted) and waits until it has let go of its files.</summary>
    public async Task CancelAsync(string recordingId)
    {
        Task? done = null;
        lock (_sync)
        {
            _pending.Remove(recordingId);
            if (_running is { } running && running.RecordingId == recordingId)
            {
                running.Run.Stop(StageStopReason.Deleted);
                running.Cancel.Cancel();
                done = running.Done;
            }
            else if (_running is null && _pending.Count == 0)
            {
                _idle.TrySetResult();
            }
        }

        if (done is not null)
        {
            await done;
        }
    }

    /// <summary>Completes when nothing is queued or running (tests, shutdown).</summary>
    public Task WhenIdleAsync()
    {
        lock (_sync)
        {
            return _idle.Task;
        }
    }

    /// <summary>App shutdown: interrupts the running stage (it is queued again for the next launch) and stops the worker.</summary>
    public async Task StopAsync()
    {
        _queue.Writer.TryComplete();
        lock (_sync)
        {
            _running?.Run.Stop(StageStopReason.Shutdown);
        }

        if (!_shutdown.IsCancellationRequested)
        {
            await _shutdown.CancelAsync();
        }

        // The stage's work is cancelled (it is queued again for the next launch); its worker need not finish the
        // native step it is in, which can take longer than the host's stop timeout.
        _workers?.KillAll();
        await _worker;
        await _sampler;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        Detach();
        _shutdown.Dispose();
    }

    /// <summary>Synchronous container disposal: signal only; <see cref="StopAsync"/> (the host's stop) did the real work.</summary>
    public void Dispose()
    {
        _queue.Writer.TryComplete();
        Detach();
        if (!_worker.IsCompleted)
        {
            lock (_sync)
            {
                _running?.Run.Stop(StageStopReason.Shutdown);
            }

            _shutdown.Cancel();

            // Interrupting a stage only removes its partial files; give it a moment before the services go away.
            _worker.Wait(TimeSpan.FromSeconds(5));
        }
    }

    internal static ProcessingRequest ApplyRemedy(ProcessingRequest request, string? remedyId, ModelCatalog? catalog = null)
    {
        switch (remedyId)
        {
            case null or "" or Remedies.Retry:
                return request;
            case Remedies.Cpu:
                return request with { ForceCpu = true };
            default:
                if (remedyId.StartsWith(Remedies.ModelPrefix, StringComparison.Ordinal))
                {
                    var modelId = remedyId[Remedies.ModelPrefix.Length..];
                    if (catalog is not null && catalog.Find(modelId) is not { Kind: ModelKinds.Transcription })
                    {
                        throw new BridgeException(DomainErrorCodes.ModelsNotFound, $"There is no transcription model called '{modelId}'. Nothing was changed.", modelId);
                    }

                    return request with { ModelId = modelId };
                }

                throw new BridgeException(BridgeErrorCodes.InvalidParams, $"Remedy '{remedyId}' is not one Memento offers. Choose one of the fixes shown with the failure.");
        }
    }

    private static TaskCompletionSource NewCompleted()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        done.SetResult();
        return done;
    }

    private void Detach()
    {
        _gate.Changed -= OnGateChanged;
        if (_models is not null)
        {
            _models.Installed -= OnModelInstalled;
        }
    }

    private async Task QueueDependentsAsync(string recordingId, CancellationToken cancellationToken)
    {
        var settings = _settings.Current;
        foreach (var dependent in _stages.Where(s => (s.Name is StageNames.Speakers or StageNames.Topics) && s.AppliesTo(settings)))
        {
            await _status.SetAsync(recordingId, StageStatusWriter.QueuedStatus(dependent.Name), cancellationToken);
        }
    }

    private async Task CancelStageAsync(string recordingId, string stage, StageStopReason reason)
    {
        Task? done = null;
        lock (_sync)
        {
            if (_running is { } running && running.RecordingId == recordingId && running.Stage == stage)
            {
                running.Run.Stop(reason);
                running.StageCancel?.Cancel();
                done = running.StageDone;
            }
        }

        if (done is not null)
        {
            await done;
        }
    }

    private async Task RecordCancelledAsync(string recordingId, string stage, CancellationToken cancellationToken)
    {
        var failure = new ProjectStageFailure(
            stage,
            stage == StageNames.Transcript ? "Transcription was cancelled." : $"The {stage} stage was cancelled.",
            "The recording is safe, and anything already finished is kept.",
            [new Remedy(Remedies.Retry, "Start again")],
            ProjectStageFailure.CauseCancelled,
            _time.GetLocalNow());
        await _status.FailAsync(recordingId, failure, "Cancelled", cancellationToken);
        await AppendQuietlyAsync(recordingId, new HistoryEntry(_time.GetLocalNow(), stage, "info", "Cancelled", failure.Kept), cancellationToken);
    }

    private void OnGateChanged(object? sender, EventArgs e)
    {
        try
        {
            _footer.Publish(force: false);
        }
#pragma warning disable CA1031 // The footer is best effort; the gate change itself must go through.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogFooterFailed(ex);
        }

        if (!_gate.IsPaused)
        {
            return;
        }

        lock (_sync)
        {
            if (_running is { Heavy: true } running)
            {
                running.Run.Stop(StageStopReason.Paused);
                running.StageCancel?.Cancel();
            }
        }
    }

    private void OnModelInstalled(object? sender, string modelId) => _ = RequeueWaitingForModelAsync();

    /// <summary>A stage that failed only because its model was missing runs as soon as a model is installed.</summary>
    private async Task RequeueWaitingForModelAsync()
    {
        try
        {
            foreach (var id in _store.ListIds())
            {
                var manifest = await _store.LoadAsync(id, CancellationToken.None);
                foreach (var failure in manifest.Failures.Where(f => f.Cause == ProjectStageFailure.CauseNoModel))
                {
                    await RetryAsync(id, failure.Stage, null, CancellationToken.None);
                }
            }
        }
#pragma warning disable CA1031 // Background convenience; the user can still retry by hand.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogRequeueFailed(ex);
        }
    }

    private void Schedule(string recordingId)
    {
        lock (_sync)
        {
            // Already waiting or already running (recovery queues it, then the launch resume finds it queued).
            if (_running?.RecordingId == recordingId)
            {
                _running.Again = true;
                return;
            }

            if (!_pending.Add(recordingId))
            {
                return;
            }

            if (_idle.Task.IsCompleted)
            {
                _idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        _queue.Writer.TryWrite(recordingId);
    }

    private async Task RunAsync()
    {
        try
        {
            await foreach (var recordingId in _queue.Reader.ReadAllAsync(_shutdown.Token))
            {
                var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                using var cancel = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
                Running running;
                lock (_sync)
                {
                    if (!_pending.Remove(recordingId))
                    {
                        // Cancelled (deleted) while it waited.
                        if (_pending.Count == 0)
                        {
                            _idle.TrySetResult();
                        }

                        continue;
                    }

                    running = new Running(recordingId, cancel, finished.Task);
                    _running = running;
                }

                try
                {
                    do
                    {
                        running.Again = false;
                        await RunStagesAsync(running, cancel.Token);
                    }
                    while (running.Again && !cancel.IsCancellationRequested);
                }
                catch (OperationCanceledException)
                {
                    // Deleted, or Memento is closing: the stage is queued again in the manifest.
                }
                catch (ProjectNotFoundException)
                {
                    // Deleted before its turn.
                }
#pragma warning disable CA1031 // One recording's failure must not stop the queue; the stage records specific failures itself.
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    LogStageFailed(ex, recordingId);
                }
                finally
                {
                    lock (_sync)
                    {
                        _running = null;
                        if (_pending.Count == 0)
                        {
                            _idle.TrySetResult();
                        }
                    }

                    finished.TrySetResult();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        finally
        {
            lock (_sync)
            {
                _idle.TrySetResult();
            }
        }
    }

    private async Task RunStagesAsync(Running running, CancellationToken cancellationToken)
    {
        foreach (var stage in _stages)
        {
            var manifest = await _store.LoadAsync(running.RecordingId, cancellationToken);
            if (StageList.Find(manifest.Stages, stage.Name) is not { } status || !StageStates.IsRunning(status.State))
            {
                continue;
            }

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (stage.IsHeavy)
                {
                    // A pass on the graphics card does not wait for a busy processor (it still waits for a recording).
                    _gate.SetHeavyOnGpu(await UsesGpuQuietlyAsync(stage, running.RecordingId, cancellationToken));
                }

                if (stage.IsHeavy && _gate.Reason is { } reason)
                {
                    // BRIDGE.md M2 clarification 4: a waiting stage stays active and keeps its percentage.
                    var current = StageList.Find((await _store.LoadAsync(running.RecordingId, cancellationToken)).Stages, stage.Name);
                    await _status.SetAsync(
                        running.RecordingId,
                        new StageStatus(stage.Name, StageStates.Active, current?.Percent ?? 0, "Paused · " + reason),
                        cancellationToken,
                        clearFailure: false);
                    LogWaiting(running.RecordingId, stage.Name, reason);
                    try
                    {
                        await _gate.WhenOpenAsync(cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        // Closing while it waits: queued again for the next launch.
                        await RequeueQuietlyAsync(running.RecordingId, stage.Name);
                        throw;
                    }

                    continue;
                }

                var run = new StageRun(running.RecordingId);
                using var stageCancel = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var stageDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                lock (_sync)
                {
                    running.Run = run;
                    running.Stage = stage.Name;
                    running.Heavy = stage.IsHeavy;
                    running.StageCancel = stageCancel;
                    running.StageDone = stageDone.Task;
                }

                try
                {
                    if (stage.IsHeavy && _gate.IsPaused)
                    {
                        continue;
                    }

                    var stopwatch = Stopwatch.StartNew();
                    await stage.RunAsync(run, stageCancel.Token);
                    LogStageRan(running.RecordingId, stage.Name, stopwatch.ElapsedMilliseconds);
                    break;
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && run.StopReason == StageStopReason.Paused)
                {
                    // The gate closed: the stage kept its partial results and runs again (resuming) once it opens.
                    continue;
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && run.StopReason == StageStopReason.Cancelled)
                {
                    var after = await _store.LoadAsync(running.RecordingId, CancellationToken.None);
                    if (StageList.Find(after.Stages, stage.Name) is { State: not StageStates.Failed })
                    {
                        await RecordCancelledAsync(running.RecordingId, stage.Name, CancellationToken.None);
                    }

                    break;
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && run.StopReason == StageStopReason.Requeued)
                {
                    // Queued again with other options (retranscribe): start the pipeline over.
                    running.Again = true;
                    return;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    if (run.StopReason == StageStopReason.Shutdown)
                    {
                        await RequeueQuietlyAsync(running.RecordingId, stage.Name);
                    }

                    throw;
                }
                finally
                {
                    lock (_sync)
                    {
                        running.StageCancel = null;
                        running.Stage = null;
                    }

                    stageDone.TrySetResult();
                }
            }
        }
    }

    private async Task<bool> UsesGpuQuietlyAsync(IProcessingStage stage, string recordingId, CancellationToken cancellationToken)
    {
        try
        {
            return await stage.UsesGpuAsync(recordingId, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ProjectNotFoundException or ProjectSchemaException)
        {
            // The stage reads the project itself and reports the problem; until then it counts as a processor stage.
            LogStageFailed(ex, recordingId);
            return false;
        }
    }

    private async Task RequeueQuietlyAsync(string recordingId, string stage)
    {
        try
        {
            var manifest = await _store.LoadAsync(recordingId, CancellationToken.None);
            if (StageList.Find(manifest.Stages, stage) is { State: StageStates.Active })
            {
                await _status.SetAsync(recordingId, StageStatusWriter.QueuedStatus(stage), CancellationToken.None, clearFailure: false);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ProjectNotFoundException)
        {
            LogStageFailed(ex, recordingId);
        }
    }

    /// <summary>Feeds the gate every second: recording active, low disk, processor load without Memento's own worker.</summary>
    private async Task SampleAsync()
    {
        var lastWall = Stopwatch.GetTimestamp();
        var lastWorkerCpu = TimeSpan.Zero;
        try
        {
            using var timer = new PeriodicTimer(SampleInterval, _time);
            while (await timer.WaitForNextTickAsync(_shutdown.Token))
            {
                try
                {
                    var settings = _settings.Current;
                    var snapshot = _probe.Sample();
                    var now = Stopwatch.GetTimestamp();
                    var workerCpu = _workers?.RunningCpuTime ?? TimeSpan.Zero;
                    var wall = Stopwatch.GetElapsedTime(lastWall, now);
                    var own = workerCpu > lastWorkerCpu && wall > TimeSpan.Zero
                        ? 100.0 * (workerCpu - lastWorkerCpu).TotalSeconds / (wall.TotalSeconds * Math.Max(1, snapshot.LogicalProcessors))
                        : 0;
                    lastWall = now;
                    lastWorkerCpu = workerCpu;
                    var free = _freeSpace.GetFreeBytes(_library.Root);
                    var lowSpace = (free is { } bytes && bytes < settings.Recording.LowSpaceThresholdBytes) || _board.ProcessingPaused == FooterStatusService.LowSpaceReason;
                    double? others = snapshot.CpuBusyPercent is { } cpu ? Math.Max(0, cpu - own) : null;
                    _gate.Sample(settings.Transcription.PauseWhenBusy, _board.Recording.Active, lowSpace, others);
                }
#pragma warning disable CA1031 // A failed sample keeps the previous gate state.
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    LogSampleFailed(ex);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    private async Task AppendQuietlyAsync(string recordingId, HistoryEntry entry, CancellationToken cancellationToken)
    {
        try
        {
            await _store.AppendHistoryAsync(recordingId, entry, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogStageFailed(ex, recordingId);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "A processing stage for recording {RecordingId} failed unexpectedly; its files are kept")]
    private partial void LogStageFailed(Exception exception, string recordingId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Recording {RecordingId}: stage {Stage} ran in {ElapsedMs} ms")]
    private partial void LogStageRan(string recordingId, string stage, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Information, Message = "Recording {RecordingId}: stage {Stage} waits ({Reason})")]
    private partial void LogWaiting(string recordingId, string stage, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Recording {RecordingId}: stage {Stage} queued again with remedy {Remedy}")]
    private partial void LogRetry(string recordingId, string stage, string remedy);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The busy watch sample failed")]
    private partial void LogSampleFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The footer could not be updated after the processing gate changed")]
    private partial void LogFooterFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Stages waiting for a model could not be queued again")]
    private partial void LogRequeueFailed(Exception exception);

    private sealed class Running(string recordingId, CancellationTokenSource cancel, Task done)
    {
        public string RecordingId { get; } = recordingId;

        public CancellationTokenSource Cancel { get; } = cancel;

        public Task Done { get; } = done;

        public StageRun Run { get; set; } = new(recordingId);

        public string? Stage { get; set; }

        public bool Heavy { get; set; }

        public CancellationTokenSource? StageCancel { get; set; }

        public Task StageDone { get; set; } = Task.CompletedTask;

        /// <summary>Scheduled again while it ran (retry, retranscribe): run the stage loop once more.</summary>
        public bool Again { get; set; }
    }
}
