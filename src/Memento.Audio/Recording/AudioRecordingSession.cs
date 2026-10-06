using System.Globalization;
using Memento.Audio.Capture;
using Memento.Audio.Sources;
using Memento.Audio.Writing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Memento.Audio.Recording;

/// <summary>
/// A multi-track recording: one capture and one <see cref="TrackWriter"/> per source, sharing a QPC session start.
/// Tracks that start with the session are padded over their start-up latency so every file begins at timeline 0;
/// pauses cut all tracks at the same capture instant; a lost source ends only its own track; stop cuts every track
/// at the stop instant. Independent of Core: an adapter maps it onto <c>IRecordingEngine</c> and the bridge events.
/// <para>
/// Threads: capture threads only fill channels; one pump task per track writes; levels and checkpoints run on
/// timers. Events are raised on thread-pool threads and never on a capture thread.
/// </para>
/// </summary>
public sealed partial class AudioRecordingSession : IAsyncDisposable
{
    private static readonly TimeSpan MinLevelInterval = TimeSpan.FromSeconds(1.0 / 30);

    private readonly AudioRecordingOptions _options;
    private readonly IAudioCaptureFactory _factory;
    private readonly Func<AudioSourceId, AudioSourceInfo?> _describe;
    private readonly ILogger _logger;
    private readonly SessionTimeline _timeline;
    private readonly object _sync = new();
    private readonly List<TrackRun> _runs = [];
    private readonly SemaphoreSlim _operations = new(1, 1);
    private readonly SemaphoreSlim _checkpointGate = new(1, 1);
    private readonly TaskCompletionSource<AudioSessionResult> _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Timer? _levelTimer;
    private Timer? _checkpointTimer;
    private AudioSessionState _state = AudioSessionState.Recording;
    private long _stopQpc;
    private int _stopStarted;
    private DateTimeOffset? _lastCheckpointAt;

    private AudioRecordingSession(AudioRecordingOptions options, IAudioCaptureFactory factory, long startQpc, DateTimeOffset startedAt)
    {
        _options = options;
        _factory = factory;
        _describe = options.Describe ?? AudioSourceEnumerator.Describe;
        _logger = (options.LoggerFactory ?? NullLoggerFactory.Instance).CreateLogger<AudioRecordingSession>();
        _timeline = new SessionTimeline(startQpc);
        StartedAt = startedAt;
    }

    /// <summary>Levels of every active track, at most 30 times per second.</summary>
    public event EventHandler<LevelsEventArgs>? Levels;

    /// <summary>After every checkpoint (timer or <see cref="CheckpointAsync"/>): what is durable on disk, with drift.</summary>
    public event EventHandler<CheckpointEventArgs>? Checkpointed;

    /// <summary>A source went away; its track has ended and the others continue.</summary>
    public event EventHandler<SourceLostEventArgs>? SourceLost;

    /// <summary>The session stopped (requested, disk full, write failure or every source lost).</summary>
    public event EventHandler<SessionStoppedEventArgs>? Stopped;

    public DateTimeOffset StartedAt { get; }

    /// <summary>Session start in <see cref="QpcClock"/> ticks; timeline 0.</summary>
    public long StartedAtQpc => _timeline.StartQpc;

    public AudioSessionState State
    {
        get
        {
            lock (_sync)
            {
                return _state;
            }
        }
    }

    /// <summary>Timeline time recorded so far (pauses excluded).</summary>
    public TimeSpan Elapsed
    {
        get
        {
            lock (_sync)
            {
                return _timeline.ToTimeline(_state is AudioSessionState.Stopping or AudioSessionState.Stopped ? _stopQpc : QpcClock.Now);
            }
        }
    }

    public DateTimeOffset? LastCheckpointAt
    {
        get
        {
            lock (_sync)
            {
                return _lastCheckpointAt;
            }
        }
    }

    public IReadOnlyList<SessionGap> Gaps => _timeline.Gaps().Select(g => new SessionGap(g.At, g.Duration)).ToList();

    /// <summary>Every track so far, active and ended.</summary>
    public IReadOnlyList<TrackStatus> Tracks
    {
        get
        {
            lock (_sync)
            {
                return _runs.Select(r => r.Status(_timeline)).ToList();
            }
        }
    }

    /// <summary>The result, once stopped.</summary>
    public Task<AudioSessionResult> Completion => _stopped.Task;

    /// <summary>
    /// Opens every source, then starts them together. If any source cannot open, none is started and
    /// <see cref="AudioSourceUnavailableException"/> names it (BRIDGE.md <c>recording.sourceUnavailable</c>).
    /// </summary>
    public static async Task<AudioRecordingSession> StartAsync(AudioRecordingOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.TracksDirectory);
        if (options.SourceIds is null || options.SourceIds.Count == 0)
        {
            throw new ArgumentException("Choose at least one source to record.", nameof(options));
        }

        var ids = options.SourceIds.Distinct(StringComparer.Ordinal).Select(AudioSourceId.Parse).ToList();
        Directory.CreateDirectory(options.TracksDirectory);
        var factory = options.CaptureFactory ?? new WasapiCaptureFactory(loggerFactory: options.LoggerFactory);
        var opens = ids.Select(id => factory.OpenAsync(id, cancellationToken)).ToList();
        try
        {
            await Task.WhenAll(opens).ConfigureAwait(false);
        }
        catch
        {
            foreach (var open in opens.Where(o => o.IsCompletedSuccessfully))
            {
                await open.Result.DisposeAsync().ConfigureAwait(false);
            }

            var failure = opens.First(o => o.IsFaulted || o.IsCanceled);
            if (failure.IsCanceled)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            throw failure.Exception!.InnerException!;
        }

        var startQpc = QpcClock.Now;
        var session = new AudioRecordingSession(options, factory, startQpc, DateTimeOffset.Now);
        var runs = ids.Select((id, i) => session.AddRun(id, opens[i].Result, gateStart: startQpc, padFrom: startQpc)).ToList();
        foreach (var run in runs)
        {
            run.Capture.Start();
            run.Completion = session.RunTrackAsync(run);
        }

        session.StartTimers();
        LogStarted(session._logger, runs.Count, options.TracksDirectory);
        return session;
    }

    public void Pause()
    {
        lock (_sync)
        {
            EnsureRunning();
            if (_timeline.IsPaused)
            {
                return;
            }

            var active = _runs.Where(r => r.IsActive).ToList();
            var now = HoldAt(active);
            _timeline.Pause(now);
            foreach (var run in active)
            {
                run.Writer.Pause(now);
                run.ReleaseHold();
            }

            _state = AudioSessionState.Paused;
        }
    }

    /// <summary>Resumes; returns the gap just closed (timeline position and duration), or null if not paused.</summary>
    public SessionGap? Resume()
    {
        lock (_sync)
        {
            EnsureRunning();
            if (!_timeline.IsPaused)
            {
                return null;
            }

            var active = _runs.Where(r => r.IsActive).ToList();
            var now = HoldAt(active);
            var gap = _timeline.Resume(now)!.Value;
            foreach (var run in active)
            {
                run.Writer.Resume(now);
                run.ReleaseHold();
            }

            _state = AudioSessionState.Recording;
            return new SessionGap(gap.At, gap.Duration);
        }
    }

    /// <summary>
    /// Freezes the pumps of <paramref name="runs"/> at "now" and returns the instant to apply, which is never
    /// earlier than the hold: no packet ending after the instant reaches a writer before the writer knows it.
    /// </summary>
    private static long HoldAt(IReadOnlyList<TrackRun> runs)
    {
        foreach (var run in runs)
        {
            run.Hold(QpcClock.Now);
        }

        return QpcClock.Now;
    }

    /// <summary>Starts or ends one track (BRIDGE.md <c>recording.setSource</c>). Returns that track's status.</summary>
    public async Task<TrackStatus> SetSourceAsync(string sourceId, bool enabled, CancellationToken cancellationToken)
    {
        var id = AudioSourceId.Parse(sourceId);
        await _operations.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            TrackRun? existing;
            lock (_sync)
            {
                EnsureRunning();
                existing = _runs.LastOrDefault(r => r.Id == id && r.IsActive);
            }

            if (enabled)
            {
                if (existing is not null)
                {
                    return existing.Status(_timeline);
                }

                var capture = await _factory.OpenAsync(id, cancellationToken).ConfigureAwait(false);
                TrackRun run;
                try
                {
                    run = _options.PadLateTracks
                        ? AddRun(id, capture, gateStart: _timeline.StartQpc, padFrom: _timeline.StartQpc)
                        : AddRun(id, capture, gateStart: QpcClock.Now, padFrom: null);
                }
                catch
                {
                    await capture.DisposeAsync().ConfigureAwait(false);
                    throw;
                }

                capture.Start();
                run.Completion = RunTrackAsync(run);
                LogSourceAdded(_logger, run.SourceId, run.FileStem);
                return run.Status(_timeline);
            }

            if (existing is null)
            {
                lock (_sync)
                {
                    return _runs.LastOrDefault(r => r.Id == id)?.Status(_timeline)
                        ?? throw new ArgumentException($"{sourceId} is not part of this recording.", nameof(sourceId));
                }
            }

            var end = HoldAt([existing]);
            existing.Ending = true;
            existing.PendingEndReason = TrackEndReason.Disabled;
            existing.PendingEndQpc = end;
            existing.Writer.End(end);
            existing.ReleaseHold();
            await DrainAsync([existing], end).ConfigureAwait(false);
            await existing.Capture.StopAsync().ConfigureAwait(false);
            await existing.Completion.ConfigureAwait(false);
            LogSourceRemoved(_logger, existing.SourceId);
            return existing.Status(_timeline);
        }
        finally
        {
            _operations.Release();
        }
    }

    /// <summary>Makes every active track durable (flush to disk, patch headers, flush) and reports drift.</summary>
    public async Task<SessionCheckpoint> CheckpointAsync(CancellationToken cancellationToken)
    {
        await _checkpointGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            List<TrackRun> active;
            lock (_sync)
            {
                active = [.. _runs.Where(r => r.EndReason is null)];
            }

            var infos = await Task.WhenAll(active.Select(r => Task.Run(
                () =>
                {
                    var c = r.Writer.Checkpoint();
                    return new TrackCheckpointInfo(r.SourceId, r.FileStem, c.Parts, c.DataBytes, c.Frames, c.Duration, r.Drift.Ppm, r.Capture.Statistics.OverrunFrames);
                },
                cancellationToken))).ConfigureAwait(false);
            var at = DateTimeOffset.Now;
            lock (_sync)
            {
                _lastCheckpointAt = at;
            }

            var checkpoint = new SessionCheckpoint(at, Elapsed, infos);
            foreach (var info in infos)
            {
                LogCheckpoint(_logger, info.SourceId, info.Duration.TotalSeconds, info.DriftPpm ?? double.NaN, info.OverrunFrames);
            }

            Raise(Checkpointed, new CheckpointEventArgs(checkpoint));
            return checkpoint;
        }
        finally
        {
            _checkpointGate.Release();
        }
    }

    /// <summary>Stops every track at the same instant and returns the result.</summary>
    public Task<AudioSessionResult> StopAsync(CancellationToken cancellationToken = default) =>
        StopCoreAsync(SessionStopReason.Requested, null).WaitAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (Volatile.Read(ref _stopStarted) == 0)
        {
            await StopCoreAsync(SessionStopReason.Requested, null).ConfigureAwait(false);
        }
        else
        {
            await _stopped.Task.ConfigureAwait(false);
        }

        _operations.Dispose();
        _checkpointGate.Dispose();
    }

    private TrackRun AddRun(AudioSourceId id, IAudioCapture capture, long gateStart, long? padFrom)
    {
        var info = _describe(id);
        lock (_sync)
        {
            var stem = TrackNaming.UniqueStem(TrackNaming.BaseStem(id, info), _options.TracksDirectory, _runs.Select(r => r.FileStem).ToList());
            var writer = new TrackWriter(new TrackWriterOptions(_options.TracksDirectory, stem, capture.Format)
            {
                RolloverBytes = _options.RolloverBytes,
                DurableCheckpoints = _options.DurableCheckpoints,
            });
            writer.SetStart(gateStart);
            if (padFrom is { } pad)
            {
                writer.PadStartFrom(pad);
            }

            if (_timeline.PausedSince is { } pausedSince)
            {
                writer.Pause(pausedSince);
            }

            var run = new TrackRun(id, info?.Name ?? id.ToString(), stem, capture, writer);
            _runs.Add(run);
            return run;
        }
    }

    private async Task RunTrackAsync(TrackRun run)
    {
        await Task.Run(() => run.PumpAsync(OnWriteFailed)).ConfigureAwait(false);

        SourceLostEventArgs? lost = null;
        var noneLeft = false;
        lock (_sync)
        {
            if (run.WriteFailure is { } failure)
            {
                run.EndReason = TrackEndReason.WriteFailed;
                run.EndedAtQpc = run.WriteFailedAtQpc;
                run.Error = failure.Message;
            }
            else if (!run.Ending && run.Capture.Loss is { } loss)
            {
                run.EndReason = TrackEndReason.SourceLost;
                run.EndedAtQpc = loss.LostAtQpc;
                var at = _timeline.ToTimeline(loss.LostAtQpc);
                run.Error = LostMessage(run, loss.Reason, loss.HResult, at);
                var remaining = _runs.Where(r => r != run && r.IsActive).Select(r => r.SourceId).ToList();
                lost = new SourceLostEventArgs(run.SourceId, run.Name, at, loss.Reason, run.Error, remaining);
                noneLeft = remaining.Count == 0 && _state is AudioSessionState.Recording or AudioSessionState.Paused;
            }
            else
            {
                run.EndReason = run.PendingEndReason ?? TrackEndReason.SessionStopped;
                run.EndedAtQpc = run.PendingEndQpc;
            }
        }

        run.CloseWriter();
        await run.Capture.DisposeAsync().ConfigureAwait(false);
        lock (_sync)
        {
            run.Result = run.BuildResult(_timeline);
        }

        if (lost is not null)
        {
            LogSourceLost(_logger, lost.SourceId, lost.Reason, lost.At.TotalSeconds);
            Raise(SourceLost, lost);
            if (noneLeft)
            {
                _ = Task.Run(() => StopCoreAsync(SessionStopReason.AllSourcesLost, $"Recording stopped at {Clock(lost.At)} because every source was lost. Everything recorded up to then is kept."));
            }
        }
    }

    private void OnWriteFailed(TrackRun run, IOException ex)
    {
        var at = _timeline.ToTimeline(run.WriteFailedAtQpc);
        var diskFull = IsDiskFull(ex);
        var message = diskFull
            ? $"The disk holding the recording is full; recording stopped at {Clock(at)}. Everything recorded up to then is kept. Free up space before recording again."
            : $"Writing {run.FileStem}.wav failed at {Clock(at)} ({ex.Message}); recording stopped. Everything recorded up to then is kept.";
        LogWriteFailed(_logger, run.SourceId, ex.HResult);
        _ = Task.Run(() => StopCoreAsync(diskFull ? SessionStopReason.DiskFull : SessionStopReason.WriteFailed, message));
    }

    private async Task<AudioSessionResult> StopCoreAsync(SessionStopReason reason, string? message)
    {
        if (Interlocked.Exchange(ref _stopStarted, 1) != 0)
        {
            return await _stopped.Task.ConfigureAwait(false);
        }

        await _operations.WaitAsync().ConfigureAwait(false);
        try
        {
            List<TrackRun> active;
            long stop;
            lock (_sync)
            {
                active = [.. _runs.Where(r => r.IsActive)];
                stop = HoldAt(active);
                _stopQpc = stop;
                _state = AudioSessionState.Stopping;
                foreach (var run in active)
                {
                    run.Ending = true;
                    run.PendingEndReason = TrackEndReason.SessionStopped;
                    run.PendingEndQpc = stop;
                    run.Writer.End(stop);
                    run.ReleaseHold();
                }
            }

            _levelTimer?.Dispose();
            _checkpointTimer?.Dispose();
            await DrainAsync(active, stop).ConfigureAwait(false);
            await Task.WhenAll(active.Select(r => r.Capture.StopAsync())).ConfigureAwait(false);
            List<TrackRun> all;
            lock (_sync)
            {
                all = [.. _runs];
            }

            await Task.WhenAll(all.Select(r => r.Completion)).ConfigureAwait(false);
            AudioSessionResult result;
            lock (_sync)
            {
                _state = AudioSessionState.Stopped;
                result = new AudioSessionResult(
                    StartedAt,
                    DateTimeOffset.Now,
                    _timeline.ToTimeline(stop),
                    Gaps,
                    all.Select(r => r.Result ?? r.BuildResult(_timeline)).ToList(),
                    reason,
                    message);
            }

            LogStopped(_logger, reason, result.Duration.TotalSeconds, result.Tracks.Count);
            _stopped.TrySetResult(result);
            Raise(Stopped, new SessionStoppedEventArgs(result));
            return result;
        }
#pragma warning disable CA1031 // The stop path must always complete the session, whatever went wrong.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _stopped.TrySetException(ex);
            throw;
        }
        finally
        {
            _operations.Release();
        }
    }

    /// <summary>Waits (bounded) until every track has seen packets captured up to <paramref name="untilQpc"/>.</summary>
    private async Task DrainAsync(IReadOnlyList<TrackRun> runs, long untilQpc)
    {
        var deadline = QpcClock.Now + _options.StopDrainTimeout.Ticks;
        while (QpcClock.Now < deadline)
        {
            if (runs.All(r => r.Writer.LastFrameEndQpc >= untilQpc || r.Completion.IsCompleted || r.Capture.Loss is not null))
            {
                return;
            }

            await Task.Delay(10).ConfigureAwait(false);
        }
    }

    private void StartTimers()
    {
        var levels = _options.LevelInterval < MinLevelInterval ? MinLevelInterval : _options.LevelInterval;
        _levelTimer = new Timer(_ => PublishLevels(), null, levels, levels);
        var checkpoint = _options.CheckpointInterval;
        if (checkpoint > TimeSpan.Zero)
        {
            _checkpointTimer = new Timer(_ => _ = TimedCheckpointAsync(), null, checkpoint, checkpoint);
        }
    }

    private async Task TimedCheckpointAsync()
    {
        try
        {
            await CheckpointAsync(CancellationToken.None).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // A failed checkpoint is logged; the next one retries and writing continues.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogCheckpointFailed(_logger, ex);
        }
    }

    private void PublishLevels()
    {
        if (Levels is null)
        {
            return;
        }

        List<TrackLevel> levels;
        lock (_sync)
        {
            if (_state is AudioSessionState.Stopping or AudioSessionState.Stopped)
            {
                return;
            }

            levels = _runs.Where(r => r.IsActive).Select(r =>
            {
                var reading = r.Meter.Take();
                return new TrackLevel(r.SourceId, reading.Rms, reading.Peak);
            }).ToList();
        }

        Raise(Levels, new LevelsEventArgs(levels));
    }

    private void Raise<T>(EventHandler<T>? handler, T args)
        where T : EventArgs
    {
        if (handler is null)
        {
            return;
        }

        try
        {
            handler(this, args);
        }
#pragma warning disable CA1031 // A subscriber's bug must not break recording.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogHandlerFailed(_logger, typeof(T).Name, ex);
        }
    }

    private void EnsureRunning()
    {
        if (_state is AudioSessionState.Stopping or AudioSessionState.Stopped)
        {
            throw new InvalidOperationException("The recording has stopped.");
        }
    }

    private static string LostMessage(TrackRun run, CaptureLostReason reason, int hr, TimeSpan at)
    {
        var what = run.Id.Kind switch
        {
            AudioSourceKind.Microphone => $"The microphone \"{run.Name}\"",
            AudioSourceKind.System => $"The output \"{run.Name}\"",
            _ => $"\"{run.Name}\"",
        };
        var happened = reason switch
        {
            CaptureLostReason.DeviceInvalidated => "was disconnected or disabled",
            CaptureLostReason.SessionDisconnected => "was disconnected by Windows",
            CaptureLostReason.ServiceStopped => "stopped because the Windows audio service stopped",
            CaptureLostReason.ProcessExited => "was closed",
            _ => string.Create(CultureInfo.InvariantCulture, $"stopped responding (0x{hr:X8})"),
        };
        return $"{what} {happened} at {Clock(at)}. Everything recorded from it before then is kept and the other tracks continue. Reconnect or reopen it and add it again to keep recording it.";
    }

    private static string Clock(TimeSpan t) => t.ToString(t.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss", CultureInfo.InvariantCulture);

    private static bool IsDiskFull(IOException ex) => (ex.HResult & 0xFFFF) is 0x70 or 0x27;

    [LoggerMessage(Level = LogLevel.Information, Message = "Recording started with {Tracks} tracks in {Directory}")]
    private static partial void LogStarted(ILogger logger, int tracks, string directory);

    [LoggerMessage(Level = LogLevel.Information, Message = "Recording stopped ({Reason}) after {Seconds:0.000} s with {Tracks} tracks")]
    private static partial void LogStopped(ILogger logger, SessionStopReason reason, double seconds, int tracks);

    [LoggerMessage(Level = LogLevel.Information, Message = "Checkpoint {Source}: {Seconds:0.000} s durable, drift {DriftPpm:0.0} ppm, overrun frames {OverrunFrames}")]
    private static partial void LogCheckpoint(ILogger logger, string source, double seconds, double driftPpm, long overrunFrames);

    [LoggerMessage(Level = LogLevel.Information, Message = "Source {Source} added mid-session as {Stem}")]
    private static partial void LogSourceAdded(ILogger logger, string source, string stem);

    [LoggerMessage(Level = LogLevel.Information, Message = "Source {Source} removed mid-session")]
    private static partial void LogSourceRemoved(ILogger logger, string source);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Source {Source} lost ({Reason}) at {Seconds:0.000} s")]
    private static partial void LogSourceLost(ILogger logger, string source, CaptureLostReason reason, double seconds);

    [LoggerMessage(Level = LogLevel.Error, Message = "Writing track {Source} failed (0x{HResult:X8}); stopping the recording")]
    private static partial void LogWriteFailed(ILogger logger, string source, int hResult);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Checkpoint failed; writing continues and the next checkpoint retries")]
    private static partial void LogCheckpointFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "A {EventArgs} handler threw; recording continues")]
    private static partial void LogHandlerFailed(ILogger logger, string eventArgs, Exception exception);
}
