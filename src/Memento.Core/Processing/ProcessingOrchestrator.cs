using System.Threading.Channels;
using Memento.Core.Library;
using Memento.Core.Projects;
using Memento.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Memento.Core.Processing;

/// <summary>
/// Runs the processing stages that follow <c>stored</c>, one recording at a time on a background worker
/// (ARCHITECTURE.md §6). M1 has no transcription or speaker stages yet, so a stored recording goes straight to the
/// last stage, <see cref="OptimizeStage"/>, when Settings asks for a smaller format. Queued work is written into the
/// manifest, so a stage interrupted by closing Memento resumes at the next launch (<see cref="ResumePendingAsync"/>).
/// </summary>
public sealed partial class ProcessingOrchestrator : IAsyncDisposable, IDisposable
{
    private readonly OptimizeStage _optimize;
    private readonly ILibraryIndex _index;
    private readonly ISettingsStore _settings;
    private readonly ILogger<ProcessingOrchestrator> _logger;
    private readonly Channel<string> _queue = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _sync = new();
    private readonly HashSet<string> _pending = new(StringComparer.Ordinal);
    private readonly Task _worker;
    private TaskCompletionSource _idle = NewCompleted();
    private (string RecordingId, CancellationTokenSource Cancel, Task Done)? _running;

    public ProcessingOrchestrator(OptimizeStage optimize, ILibraryIndex index, ISettingsStore settings, ILogger<ProcessingOrchestrator> logger)
    {
        _optimize = optimize;
        _index = index;
        _settings = settings;
        _logger = logger;
        _worker = Task.Run(RunAsync);
    }

    /// <summary>The recording has stage work queued or running, so it must not be deleted underneath it.</summary>
    public bool IsBusy(string recordingId)
    {
        lock (_sync)
        {
            return _pending.Contains(recordingId) || _running?.RecordingId == recordingId;
        }
    }

    /// <summary>
    /// Called once <c>stored</c> is done: queues the stages that apply with the current settings (in M1 only
    /// <c>optimize</c>, for AAC or MP3) and marks them queued in the manifest. Does nothing for lossless FLAC.
    /// </summary>
    public async Task EnqueueAfterStoredAsync(string recordingId, CancellationToken cancellationToken)
    {
        if (!OptimizeStage.AppliesTo(_settings.Current.Recording.Storage))
        {
            return;
        }

        await _optimize.MarkQueuedAsync(recordingId, cancellationToken);
        Schedule(recordingId);
    }

    /// <summary>Launch: queues every recording whose stage work was queued or running when Memento closed.</summary>
    public async Task<int> ResumePendingAsync(CancellationToken cancellationToken)
    {
        var count = 0;
        foreach (var entry in await _index.ListProcessingAsync(cancellationToken))
        {
            if (entry.Stages.Any(s => s.Stage == StageNames.Optimize && StageStates.IsRunning(s.State)))
            {
                Schedule(entry.Summary.Id);
                count++;
            }
        }

        return count;
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
        if (!_shutdown.IsCancellationRequested)
        {
            await _shutdown.CancelAsync();
        }

        await _worker;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _shutdown.Dispose();
    }

    /// <summary>Synchronous container disposal: signal only; <see cref="StopAsync"/> (the host's stop) did the real work.</summary>
    public void Dispose()
    {
        _queue.Writer.TryComplete();
        if (!_worker.IsCompleted)
        {
            _shutdown.Cancel();

            // Interrupting a stage only removes its partial files; give it a moment before the services go away.
            _worker.Wait(TimeSpan.FromSeconds(5));
        }
    }

    private static TaskCompletionSource NewCompleted()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        done.SetResult();
        return done;
    }

    private void Schedule(string recordingId)
    {
        lock (_sync)
        {
            // Already waiting or already running (recovery queues it, then the launch resume finds it queued).
            if (_running?.RecordingId == recordingId || !_pending.Add(recordingId))
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

                    _running = (recordingId, cancel, finished.Task);
                }

                try
                {
                    await _optimize.RunAsync(recordingId, cancel.Token);
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

    [LoggerMessage(Level = LogLevel.Error, Message = "A processing stage for recording {RecordingId} failed unexpectedly; its files are kept")]
    private partial void LogStageFailed(Exception exception, string recordingId);
}
