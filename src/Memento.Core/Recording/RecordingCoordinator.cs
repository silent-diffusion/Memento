using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Formatting;
using Memento.Core.Host;
using Memento.Core.Library;
using Memento.Core.Processing;
using Memento.Core.Projects;
using Memento.Core.Settings;
using Memento.Core.Status;
using Microsoft.Extensions.Logging;

namespace Memento.Core.Recording;

/// <summary>
/// The one place that runs a recording session: creates the project, keeps <c>recording.state.json</c> current
/// (start, every checkpoint, every state change), watches free space, turns engine events into bridge events, and on
/// stop hands the project to <see cref="ProjectFinalizationService"/>. One session at a time; a second
/// <c>recording.start</c> is refused. Nothing here runs on a capture thread: engine events arrive through the
/// session's dispatcher.
/// </summary>
public sealed partial class RecordingCoordinator : IAsyncDisposable, IDisposable
{
    private const int MaxTitleLength = 200;
    private const int MaxTypeLength = 64;
    private const int MaxNoteLength = 2000;

    private readonly IRecordingEngine _engine;
    private readonly IAudioSourceProvider _sources;
    private readonly IProjectStore _store;
    private readonly ProjectCatalog _catalog;
    private readonly ProjectFinalizationService _finalization;
    private readonly ProcessingOrchestrator _processing;
    private readonly ISettingsStore _settings;
    private readonly ILibraryLocation _library;
    private readonly IFreeSpaceProbe _freeSpace;
    private readonly BridgeEventPublisher _publisher;
    private readonly RecordingStatusBoard _board;
    private readonly FooterStatusService _footer;
    private readonly RecordingCoordinatorOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<RecordingCoordinator> _logger;
    private readonly LibraryOpener? _opener;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentDictionary<string, Finalizing> _finalizing = new(StringComparer.Ordinal);
    private ActiveRecording? _active;

    public RecordingCoordinator(
        IRecordingEngine engine,
        IAudioSourceProvider sources,
        IProjectStore store,
        ProjectCatalog catalog,
        ProjectFinalizationService finalization,
        ProcessingOrchestrator processing,
        ISettingsStore settings,
        ILibraryLocation library,
        IFreeSpaceProbe freeSpace,
        BridgeEventPublisher publisher,
        RecordingStatusBoard board,
        FooterStatusService footer,
        RecordingCoordinatorOptions options,
        TimeProvider time,
        ILogger<RecordingCoordinator> logger,
        LibraryOpener? opener = null)
    {
        _opener = opener;
        _engine = engine;
        _sources = sources;
        _store = store;
        _catalog = catalog;
        _finalization = finalization;
        _processing = processing;
        _settings = settings;
        _library = library;
        _freeSpace = freeSpace;
        _publisher = publisher;
        _board = board;
        _footer = footer;
        _options = options;
        _time = time;
        _logger = logger;
    }

    /// <summary>The active session, else the most recent one still finalizing, else <c>null</c> (<c>recording.current</c>).</summary>
    public RecordingStatePayload? Current
    {
        get
        {
            if (Volatile.Read(ref _active) is { } active)
            {
                return BuildPayload(active, StateName(active.Session.State), active.Session.Tracks, active.Session.ElapsedMs);
            }

            // A finalize whose outcome was published is over for the page, even while processing is still being queued:
            // answering its "finalizing" payload then would leave a page that rejoins it waiting for an event already sent.
            return _finalizing.Values.Where(f => !f.Finished).OrderByDescending(f => f.Payload.StartedAt).FirstOrDefault()?.Payload;
        }
    }

    /// <summary>The recording is being captured or finalized, so it must not be deleted.</summary>
    public bool IsBusy(string recordingId) =>
        (Volatile.Read(ref _active)?.RecordingId == recordingId) || _finalizing.ContainsKey(recordingId);

    /// <summary>Completes when every finalize started so far has finished (tests, shutdown).</summary>
    public async Task WhenIdleAsync()
    {
        // A host stop in progress holds the gate until its finalize is registered.
        await _gate.WaitAsync();
        _gate.Release();
        await Task.WhenAll(_finalizing.Values.Select(f => f.Task));
    }

    public async Task<RecordingStartResult> StartAsync(RecordingStartParams parameters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_active is { } running)
            {
                throw new BridgeException(
                    DomainErrorCodes.RecordingAlreadyActive,
                    $"\"{running.Title}\" is still recording (started {running.StartedAt.ToString("t", CultureInfo.CurrentCulture)}). Stop it before starting another; it keeps recording until you do.",
                    running.SessionId);
            }

            var sourceIds = (parameters.SourceIds ?? []).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToList();
            if (sourceIds.Count == 0)
            {
                throw new BridgeException(
                    DomainErrorCodes.RecordingNoSources,
                    "Choose at least one audio source to record. Nothing was started.");
            }

            var type = ValidateType(parameters.Type);
            var title = ValidateTitle(parameters.Title, type);
            var sources = await ResolveSourcesAsync(sourceIds, cancellationToken);
            if (_opener is not null)
            {
                await _opener.EnsureOpenAsync("The recording did not start and nothing was recorded.", cancellationToken);
            }

            EnsureRoomToStart();

            var manifest = await _store.CreateAsync(new ProjectCreateRequest(title, type, _time.GetLocalNow(), ProjectStates.Recording), cancellationToken);
            var folder = _store.GetProjectFolder(manifest.Id);
            var plan = new RecordingPlan(folder, sources, TimeSpan.FromSeconds(_settings.Current.Recording.CheckpointSeconds));
            IRecordingSession session;
            try
            {
                session = await _engine.StartAsync(plan, cancellationToken);
            }
            catch (SourceUnavailableException ex)
            {
                await DiscardEmptyProjectAsync(manifest.Id);
                throw new BridgeException(
                    DomainErrorCodes.RecordingSourceUnavailable,
                    $"{ex.SourceName} could not be opened ({ex.Reason}), so the recording did not start and no other source was started. Check the device, or turn it off and start again.",
                    ex.SourceId);
            }
            catch (IOException ex) when (Audio.DiskErrors.IsDiskFull(ex))
            {
                await DiscardEmptyProjectAsync(manifest.Id);
                throw DiskFullAtStart(_freeSpace.GetFreeBytes(_library.Root));
            }

            var active = new ActiveRecording(session, manifest.Id, title, plan.CheckpointInterval);
            Subscribe(active);
            Volatile.Write(ref _active, active);

            var tracks = session.Tracks;
            await WriteStateFileAsync(active, "recording", tracks, null);
            await _catalog.SaveAsync(manifest with { Tracks = tracks.Select(SessionTrackMapper.ToManifest).ToList() }, cancellationToken);
            await _store.AppendHistoryAsync(
                manifest.Id,
                new HistoryEntry(
                    session.StartedAt,
                    "recorded",
                    "started",
                    "Recording started",
                    $"{HumanFormat.Count(tracks.Count, "audio track", "audio tracks")}: {string.Join(", ", tracks.Select(t => t.Source.Name))} · engine {_engine.Name}"),
                cancellationToken);
            await RememberSelectionAsync(sourceIds, type, cancellationToken);

            UpdateBoard(active);
            _footer.Publish(force: true);
            active.Loops = Task.WhenAll(
                Task.Run(() => RunTickerAsync(active, active.Stopping.Token), CancellationToken.None),
                Task.Run(() => RunDiskWatchAsync(active, active.Stopping.Token), CancellationToken.None));
            _publisher.PublishRecordingState(BuildPayload(active, "recording", tracks, 0));
            LogStarted(manifest.Id, session.SessionId, tracks.Count, _engine.Name);
            return new RecordingStartResult(session.SessionId, manifest.Id, session.StartedAt);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<Track>> SetSourceAsync(string sessionId, string sourceId, bool enabled, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var active = Require(sessionId);
            AudioSource source;
            var known = active.Session.Tracks.LastOrDefault(t => t.Source.Id == sourceId)?.Source;
            if (enabled)
            {
                var available = await _sources.ListAsync(cancellationToken);
                source = available.FirstOrDefault(s => s.Id == sourceId)
                    ?? throw SourceUnavailable(known?.Name ?? sourceId, sourceId, "it is not connected", recording: true);
            }
            else
            {
                if (known is null)
                {
                    return active.Session.Tracks.Select(SessionTrackMapper.ToContract).ToList();
                }

                source = known;
            }

            IReadOnlyList<SessionTrack> tracks;
            try
            {
                tracks = await active.Session.SetSourceAsync(source, enabled, cancellationToken);
            }
            catch (SourceUnavailableException ex)
            {
                throw SourceUnavailable(ex.SourceName, ex.SourceId, ex.Reason, recording: true);
            }

            await _catalog.UpdateAsync(active.RecordingId, m => m with { Tracks = tracks.Select(SessionTrackMapper.ToManifest).ToList() }, cancellationToken);
            await WriteStateFileAsync(active, StateName(active.Session.State), tracks, active.LastCheckpointAt);
            _publisher.PublishRecordingState(BuildPayload(active, StateName(active.Session.State), tracks, active.Session.ElapsedMs));
            return tracks.Select(SessionTrackMapper.ToContract).ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task PauseAsync(string sessionId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await Require(sessionId).Session.PauseAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ResumeAsync(string sessionId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await Require(sessionId).Session.ResumeAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Highlight> MarkHighlightAsync(string sessionId, string? note, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var active = Require(sessionId);
            var text = (note ?? string.Empty).Trim();
            if (text.Length > MaxNoteLength)
            {
                throw new BridgeException(BridgeErrorCodes.InvalidParams, $"A highlight note can be at most {MaxNoteLength} characters; this one has {text.Length}.");
            }

            var at = await active.Session.MarkAsync(cancellationToken);
            var highlight = new Highlight(AnnotationIds.New('h'), at, text, AnnotationOrigins.User, null);
            await _store.UpdateAnnotationsAsync(
                active.RecordingId,
                doc => doc with { Highlights = doc.Highlights.Append(highlight).OrderBy(h => h.AtMs).ToList() },
                cancellationToken);
            Interlocked.Increment(ref active.HighlightsCount);
            await _catalog.TouchedAsync(active.RecordingId, cancellationToken);
            _publisher.PublishRecordingState(BuildPayload(active, StateName(active.Session.State), active.Session.Tracks, active.Session.ElapsedMs));
            return highlight;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Stops the session and starts finalize in the background; returns the recording id.</summary>
    public async Task<string> StopAsync(string sessionId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var active = Require(sessionId);
            var result = await active.Session.StopAsync(CancellationToken.None);
            if (!await CompleteLockedAsync(active, result, result.StoppedBy, result.ElapsedMs))
            {
                var folder = _store.GetProjectFolder(active.RecordingId);
                throw new BridgeException(
                    DomainErrorCodes.LibraryUnavailable,
                    $"The recording stopped at {HumanFormat.Clock(result.ElapsedMs)}, but its folder {folder} could not be reached, so it is not finished yet. Everything recorded is kept in its files; Memento finishes it the next time it starts with the library's drive connected.",
                    folder);
            }

            return active.RecordingId;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Crash handler: flushes every writer and patches headers, waiting at most <paramref name="timeout"/>.
    /// Never throws.
    /// </summary>
    public void FlushForCrash(TimeSpan timeout)
    {
        if (Volatile.Read(ref _active) is not { } active)
        {
            return;
        }

        try
        {
            active.Session.CheckpointAsync(CancellationToken.None).Wait(timeout);
        }
#pragma warning disable CA1031 // The process is going down; a failed flush must not mask the crash.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogCrashFlushFailed(ex);
        }
    }

    /// <summary>
    /// App shutdown: an active session is stopped cleanly (headers patched) and its <c>recording.state.json</c> kept,
    /// so the next launch finalizes it and says so. Finalizes already running are awaited until <paramref name="cancellationToken"/>.
    /// </summary>
    public async Task ShutdownAsync(CancellationToken cancellationToken)
    {
        var active = Interlocked.Exchange(ref _active, null);
        if (active is not null)
        {
            await active.StopLoopsAsync();
            try
            {
                var result = await active.Session.StopAsync(CancellationToken.None);
                await WriteStateFileAsync(active, "stopped", result.Tracks, active.LastCheckpointAt);
                LogStoppedAtShutdown(active.RecordingId, result.ElapsedMs);
            }
#pragma warning disable CA1031 // Shutdown must go on; the files stay for recovery.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogShutdownStopFailed(ex, active.RecordingId);
            }

            await active.Session.DisposeAsync();
        }

        try
        {
            await WhenIdleAsync().WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Unfinished finalizes keep their recording.state.json and complete at the next launch.
        }
    }

    public async ValueTask DisposeAsync()
    {
        await ShutdownAsync(CancellationToken.None);
        _gate.Dispose();
    }

    public void Dispose()
    {
        // Synchronous container disposal: stop timers only. ShutdownAsync (the host's stop) did the real work.
        if (Volatile.Read(ref _active) is { } active)
        {
            active.Stopping.Cancel();
        }
    }

    private static string StateName(RecordingSessionState state) => state switch
    {
        RecordingSessionState.Paused => "paused",
        RecordingSessionState.Stopped => "stopped",
        _ => "recording",
    };

    private static string ReasonName(HostStopReason reason) => reason switch
    {
        HostStopReason.DiskFull => "diskFull",
        HostStopReason.DeviceLost => "deviceLost",
        _ => "error",
    };

    private static string ValidateType(string? type)
    {
        var value = (type ?? string.Empty).Trim();
        if (value.Length is 0 or > MaxTypeLength)
        {
            throw new BridgeException(BridgeErrorCodes.InvalidParams, $"A recording type needs a name of 1 to {MaxTypeLength} characters, such as \"meeting\".");
        }

        return value;
    }

    private static string ValidateTitle(string? title, string type)
    {
        var value = (title ?? string.Empty).Trim();
        if (value.Length > MaxTitleLength)
        {
            throw new BridgeException(BridgeErrorCodes.InvalidParams, $"A title can be at most {MaxTitleLength} characters; this one has {value.Length}.");
        }

        return value.Length == 0 ? $"Untitled {type}" : value;
    }

    private static BridgeException SourceUnavailable(string name, string sourceId, string reason, bool recording) =>
        new(
            DomainErrorCodes.RecordingSourceUnavailable,
            recording
                ? $"{name} could not be opened ({reason}). The other tracks keep recording. Check the device and turn it on again."
                : $"{name} could not be opened ({reason}), so the recording did not start and no other source was started. Check the device, or turn it off and start again.",
            sourceId);

    private static BridgeException DiskFullAtStart(long? free) =>
        new(
            DomainErrorCodes.RecordingDiskFull,
            $"The library drive has only {HumanFormat.Bytes(free ?? 0)} free, so the recording did not start. Nothing was recorded. Free up space and try again.");

    private async Task<List<AudioSource>> ResolveSourcesAsync(List<string> sourceIds,CancellationToken cancellationToken)
    {
        var available = await _sources.ListAsync(cancellationToken);
        var resolved = new List<AudioSource>(sourceIds.Count);
        foreach (var id in sourceIds)
        {
            var source = available.FirstOrDefault(s => s.Id == id)
                ?? throw SourceUnavailable(id.StartsWith("mic:", StringComparison.Ordinal) ? "That microphone" : "That source", id, "it is not connected", recording: false);
            resolved.Add(source);
        }

        return resolved;
    }

    private void EnsureRoomToStart()
    {
        var free = _freeSpace.GetFreeBytes(_library.Root);
        if (free is not null && free < _options.StopFloorBytes)
        {
            throw DiskFullAtStart(free);
        }
    }

    private async Task DiscardEmptyProjectAsync(string recordingId)
    {
        try
        {
            // Nothing was recorded into it: no audio, no state file. Removing it is not deleting user data.
            await _store.DeleteAsync(recordingId, CancellationToken.None);
        }
        catch (IOException ex)
        {
            LogDiscardFailed(ex, recordingId);
        }
    }

    private async Task RememberSelectionAsync(IReadOnlyList<string> sourceIds, string type, CancellationToken cancellationToken)
    {
        try
        {
            await _settings.UpdateAsync(
                s => s with { Recording = s.Recording with { DefaultSourceIds = sourceIds.ToList(), DefaultType = type } },
                cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            LogRememberFailed(ex);
        }
    }

    private ActiveRecording Require(string sessionId)
    {
        if (_active is { } active && string.Equals(active.SessionId, sessionId, StringComparison.Ordinal))
        {
            return active;
        }

        throw new BridgeException(
            DomainErrorCodes.RecordingNoSession,
            "That recording session has already ended. Anything it recorded is saved in the Library.",
            sessionId);
    }

    private void Subscribe(ActiveRecording active)
    {
        var session = active.Session;
        session.LevelsAvailable += (_, e) => OnLevels(active, e);
        session.StateChanged += (_, e) => OnStateChanged(active, e);
        session.CheckpointWritten += (_, e) => _ = OnCheckpointAsync(active, e);
        session.SourceLost += (_, e) => _ = OnSourceLostAsync(active, e);
        session.HostStopped += (_, e) => _ = HandleHostStopAsync(active, e.Reason, e.AtMs, e.Detail);
    }

    private void OnLevels(ActiveRecording active, LevelsEventArgs e)
    {
        if (active.Completed)
        {
            return;
        }

        var now = Stopwatch.GetTimestamp();
        if (Stopwatch.GetElapsedTime(Interlocked.Read(ref active.LastLevelsTimestamp), now) < _options.LevelsMinInterval)
        {
            return;
        }

        Interlocked.Exchange(ref active.LastLevelsTimestamp, now);
        _publisher.PublishRecordingLevels(new RecordingLevelsPayload(active.SessionId, e.Levels));
    }

    private void OnStateChanged(ActiveRecording active, SessionStateChangedEventArgs e)
    {
        if (active.Completed || e.State == RecordingSessionState.Stopped)
        {
            return;
        }

        var tracks = active.Session.Tracks;
        _ = WriteStateFileAsync(active, StateName(e.State), tracks, active.LastCheckpointAt);
        _publisher.PublishRecordingState(BuildPayload(active, StateName(e.State), tracks, e.ElapsedMs));
        UpdateBoard(active);
        _footer.Publish(force: false);
    }

    private async Task OnCheckpointAsync(ActiveRecording active, CheckpointEventArgs e)
    {
        if (active.Completed)
        {
            return;
        }

        active.LastCheckpointAt = e.At;
        await WriteStateFileAsync(active, StateName(active.Session.State), e.Tracks, e.At);
        UpdateBoard(active);
        _footer.Publish(force: false);
    }

    private async Task OnSourceLostAsync(ActiveRecording active, SourceLostEventArgs e)
    {
        if (active.Completed)
        {
            return;
        }

        active.LostSource = e.Track.Source.Name;
        var tracks = active.Session.Tracks;
        try
        {
            await _catalog.UpdateAsync(active.RecordingId, m => m with { Tracks = tracks.Select(SessionTrackMapper.ToManifest).ToList() }, CancellationToken.None);
        }
        catch (IOException ex)
        {
            LogManifestWriteFailed(ex, active.RecordingId);
        }

        await WriteStateFileAsync(active, StateName(active.Session.State), tracks, active.LastCheckpointAt);
        _publisher.PublishSourceLost(new SourceLostPayload(
            active.SessionId,
            e.Track.Source.Id,
            e.Track.Source.Name,
            e.AtMs,
            e.Remaining.Select(t => t.Source.Name).ToList()));
        UpdateBoard(active);
        _footer.Publish(force: false);
        LogSourceLost(active.RecordingId, e.Track.TrackId, e.AtMs);
    }

    private async Task HandleHostStopAsync(ActiveRecording active, HostStopReason reason, long? atMs, string detail)
    {
        await _gate.WaitAsync();
        try
        {
            if (!ReferenceEquals(_active, active))
            {
                return;
            }

            var result = await active.Session.StopAsync(CancellationToken.None);
            var at = atMs ?? result.ElapsedMs;
            LogHostStopped(active.RecordingId, reason.ToString(), at, detail);
            _publisher.PublishStoppedByHost(new StoppedByHostPayload(active.SessionId, active.RecordingId, ReasonName(reason), at, HostStopMessage(reason, at)));
            await CompleteLockedAsync(active, result, reason, at);
        }
#pragma warning disable CA1031 // This runs from an event; a failure here must be logged, not crash the host.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogHostStopFailed(ex, active.RecordingId);
        }
        finally
        {
            _gate.Release();
        }
    }

    private string HostStopMessage(HostStopReason reason, long atMs)
    {
        var at = HumanFormat.Clock(atMs);
        var drive = Path.GetPathRoot(_library.Root)?.TrimEnd('\\') ?? "the library drive";
        return reason switch
        {
            HostStopReason.DiskFull =>
                $"Stopped · drive full at {at}. Everything up to that point is saved and will transcribe once there is room. Free up space on {drive} to record again.",
            HostStopReason.DeviceLost =>
                $"Recording stopped at {at} because every audio source was disconnected. Everything up to that point is saved. Reconnect a device to record again.",
            _ =>
                $"Recording stopped at {at} because a track could not be written. Everything up to that point is saved; the error was written to the Memento log.",
        };
    }

    /// <summary>
    /// Called with <see cref="_gate"/> held, once per session. Returns <c>false</c> when the project folder could not
    /// be written (its drive went away): the session is over and the page is told, the files and
    /// <c>recording.state.json</c> stay as they are, and recovery finishes the recording at the next launch.
    /// </summary>
    private async Task<bool> CompleteLockedAsync(ActiveRecording active, RecordingSessionResult result, HostStopReason? reason, long atMs)
    {
        if (active.Completed)
        {
            return true;
        }

        active.Completed = true;
        Volatile.Write(ref _active, null);
        await active.StopLoopsAsync();
        try
        {
            await RecordStopLockedAsync(active, result, reason, atMs);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ProjectNotFoundException)
        {
            LogStopNotSaved(ex, active.RecordingId);
            _board.SetRecording(RecordingFooterStatus.Idle);
            _board.SetProcessingPaused(null);
            _footer.Publish(force: true);
            _publisher.PublishRecordingState(BuildPayload(active, "stopped", result.Tracks, result.ElapsedMs));
            _ = Task.Run(() => active.Session.DisposeAsync().AsTask(), CancellationToken.None);
            return false;
        }
    }

    private async Task RecordStopLockedAsync(ActiveRecording active, RecordingSessionResult result, HostStopReason? reason, long atMs)
    {
        var tracks = result.Tracks;
        var pauses = result.Pauses.Select(p => new ProjectPause(p.AtMs, p.PausedAt, p.DurationMs ?? 0)).ToList();
        await _catalog.UpdateAsync(
            active.RecordingId,
            m => m with
            {
                Tracks = tracks.Select(SessionTrackMapper.ToManifest).ToList(),
                Pauses = pauses,
                DurationMs = result.ElapsedMs,
                State = ProjectStates.Finalizing,
                Stages = ProjectFinalizationService.WithStored(m.Stages, StageStates.Active, 0, "Saving tracks"),
            },
            CancellationToken.None);
        await WriteStateFileAsync(active, "stopped", tracks, active.LastCheckpointAt);

        var summary = reason switch
        {
            HostStopReason.DiskFull => $"Stopped · drive full at {HumanFormat.Clock(atMs)}",
            HostStopReason.DeviceLost => $"Stopped at {HumanFormat.Clock(atMs)} · every source was disconnected",
            HostStopReason.Error => $"Stopped at {HumanFormat.Clock(atMs)} · a track could not be written",
            _ => $"Recorded {HumanFormat.Clock(result.ElapsedMs)}",
        };
        var detail = $"{HumanFormat.Count(tracks.Count, "track", "tracks")}"
            + (pauses.Count > 0 ? $" · paused {HumanFormat.Count(pauses.Count, "time", "times")}" : string.Empty)
            + string.Concat(tracks.Where(t => t.EndedAtMs is not null).Select(t => $" · {t.Source.Name} ended at {HumanFormat.Clock(t.EndedAtMs!.Value)}"));
        await _store.AppendHistoryAsync(
            active.RecordingId,
            new HistoryEntry(result.StoppedAt, "recorded", reason is null ? "completed" : "info", summary, detail),
            CancellationToken.None);

        _board.SetRecording(RecordingFooterStatus.Idle);
        _board.SetProcessingPaused(null);
        _footer.Publish(force: true);

        var payload = BuildPayload(active, "finalizing", tracks, result.ElapsedMs);
        _publisher.PublishRecordingState(payload);
        var finalizing = new Finalizing(payload);
        _finalizing[active.RecordingId] = finalizing;
        finalizing.Task = Task.Run(() => FinalizeInBackgroundAsync(active, finalizing), CancellationToken.None);
        if (reason is { } byHost)
        {
            LogStoppedByHost(active.RecordingId, result.ElapsedMs, byHost);
        }
        else
        {
            LogStopped(active.RecordingId, result.ElapsedMs);
        }

        // Not awaited here: this can run on the session's own event thread, which disposing waits for.
        _ = Task.Run(() => active.Session.DisposeAsync().AsTask(), CancellationToken.None);
    }

    /// <summary>The stored recording is safe; later stages are best effort and must not turn it into a failure.</summary>
    private async Task QueueProcessingAsync(string recordingId)
    {
        try
        {
            await _processing.EnqueueAfterStoredAsync(recordingId, CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ProjectNotFoundException)
        {
            LogQueueFailed(ex, recordingId);
        }
    }

    private async Task FinalizeInBackgroundAsync(ActiveRecording active, Finalizing finalizing)
    {
        try
        {
            var manifest = await _finalization.FinalizeAsync(active.RecordingId, ProjectStates.Ready, CancellationToken.None);
            var state = manifest.State == ProjectStates.Failed ? "stopped" : "ready";
            finalizing.Finished = true;
            _publisher.PublishRecordingState(finalizing.Payload with
            {
                State = state,
                Tracks = manifest.Tracks.Select(ProjectMapper.ToTrack).ToList(),
            });
            if (manifest.State != ProjectStates.Failed)
            {
                await QueueProcessingAsync(active.RecordingId);
            }
        }
#pragma warning disable CA1031 // Background work: log and keep the files; recovery retries at the next launch.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogFinalizeFailed(ex, active.RecordingId);
            try
            {
                // Say so in the Library; the files and recording.state.json stay, and the next launch retries.
                await _catalog.UpdateAsync(
                    active.RecordingId,
                    m => m with { State = ProjectStates.Failed, Stages = ProjectFinalizationService.WithStored(m.Stages, StageStates.Failed, null, "Saving failed") },
                    CancellationToken.None);
            }
            catch (Exception inner) when (inner is IOException or UnauthorizedAccessException or ProjectNotFoundException)
            {
                LogFinalizeFailed(inner, active.RecordingId);
            }

            finalizing.Finished = true;
            _publisher.PublishRecordingState(finalizing.Payload with { State = "stopped" });
        }
        finally
        {
            _finalizing.TryRemove(active.RecordingId, out _);
        }
    }

    private async Task RunTickerAsync(ActiveRecording active, CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(_options.StateTickInterval, _time);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                if (active.Session.State == RecordingSessionState.Recording && !active.Completed)
                {
                    _publisher.PublishRecordingState(BuildPayload(active, "recording", active.Session.Tracks, active.Session.ElapsedMs));
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Session over.
        }
    }

    private async Task RunDiskWatchAsync(ActiveRecording active, CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(_options.DiskSampleInterval, _time);
            do
            {
                SampleDisk(active);
            }
            while (await timer.WaitForNextTickAsync(cancellationToken));
        }
        catch (OperationCanceledException)
        {
            // Session over.
        }
    }

    private void SampleDisk(ActiveRecording active)
    {
        if (active.Completed)
        {
            return;
        }

        var free = _freeSpace.GetFreeBytes(_library.Root);
        if (free is not { } bytes)
        {
            return;
        }

        if (bytes < _options.StopFloorBytes)
        {
            LogStopFloor(active.RecordingId, bytes);
            _ = HandleHostStopAsync(active, HostStopReason.DiskFull, null, $"{bytes} bytes free, below the {_options.StopFloorBytes}-byte floor");
            return;
        }

        var threshold = _settings.Current.Recording.LowSpaceThresholdBytes;
        if (bytes < threshold)
        {
            if (!active.LowSpaceRaised)
            {
                active.LowSpaceRaised = true;
                _board.SetProcessingPaused(FooterStatusService.LowSpaceReason);
                _publisher.PublishLowSpace(new StorageLowSpacePayload(bytes, threshold, RecordingContinues: true, TranscriptionPaused: true));
                _footer.Publish(force: false);
                LogLowSpace(active.RecordingId, bytes, threshold);
            }
        }
        else if (active.LowSpaceRaised)
        {
            active.LowSpaceRaised = false;
            _board.SetProcessingPaused(null);
            _footer.Publish(force: false);
        }
    }

    private async Task WriteStateFileAsync(ActiveRecording active, string state, IReadOnlyList<SessionTrack> tracks, DateTimeOffset? lastCheckpointAt)
    {
        await active.StateFileGate.WaitAsync();
        try
        {
            await _store.WriteRecordingStateAsync(
                new RecordingStateDocument
                {
                    SessionId = active.SessionId,
                    RecordingId = active.RecordingId,
                    StartedAt = active.StartedAt,
                    State = state,
                    LastCheckpointAt = lastCheckpointAt,
                    ElapsedMsAtCheckpoint = tracks.Count == 0 ? 0 : tracks.Max(t => t.StartOffsetMs + t.Format.BytesToMilliseconds(t.CheckpointedBytes)),
                    CheckpointSeconds = (int)active.CheckpointInterval.TotalSeconds,
                    FlushIntervalMs = _engine.FlushInterval is { } flush ? (int)flush.TotalMilliseconds : null,
                    Tracks = tracks.Select(SessionTrackMapper.ToState).ToList(),
                    Pauses = active.Session.Pauses.Select(p => new ProjectPause(p.AtMs, p.PausedAt, p.DurationMs ?? 0)).ToList(),
                },
                CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Capture keeps going; the previous state file (or the WAV headers) still lets recovery work.
            LogStateFileFailed(ex, active.RecordingId);
        }
        finally
        {
            active.StateFileGate.Release();
        }
    }

    private void UpdateBoard(ActiveRecording active) =>
        _board.SetRecording(new RecordingFooterStatus(Active: !active.Completed, active.LastCheckpointAt, active.LostSource));

    private static RecordingStatePayload BuildPayload(ActiveRecording active, string state, IReadOnlyList<SessionTrack> tracks, long elapsedMs) =>
        new(
            active.SessionId,
            active.RecordingId,
            state,
            active.StartedAt,
            elapsedMs,
            tracks.Select(SessionTrackMapper.ToContract).ToList(),
            active.LastCheckpointAt ?? active.Session.LastCheckpointAt,
            Volatile.Read(ref active.HighlightsCount));

    [LoggerMessage(Level = LogLevel.Information, Message = "Recording {RecordingId} started (session {SessionId}, {Tracks} tracks, engine {Engine})")]
    private partial void LogStarted(string recordingId, string sessionId, int tracks, string engine);

    [LoggerMessage(Level = LogLevel.Information, Message = "Recording {RecordingId} stopped at {ElapsedMs} ms by the user; finalizing")]
    private partial void LogStopped(string recordingId, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Information, Message = "Recording {RecordingId} stopped at {ElapsedMs} ms by the host ({Reason}); finalizing")]
    private partial void LogStoppedByHost(string recordingId, long elapsedMs, HostStopReason reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Recording {RecordingId} stopped by the host ({Reason}) at {AtMs} ms: {Detail}")]
    private partial void LogHostStopped(string recordingId, string reason, long atMs, string detail);

    [LoggerMessage(Level = LogLevel.Error, Message = "Recording {RecordingId} stopped, but its folder could not be written; it stays for recovery at the next launch")]
    private partial void LogStopNotSaved(Exception exception, string recordingId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Handling the host stop of recording {RecordingId} failed")]
    private partial void LogHostStopFailed(Exception exception, string recordingId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Recording {RecordingId}: track {TrackId} lost its source at {AtMs} ms")]
    private partial void LogSourceLost(string recordingId, string trackId, long atMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Recording {RecordingId}: {FreeBytes} bytes free, below the {ThresholdBytes}-byte warning")]
    private partial void LogLowSpace(string recordingId, long freeBytes, long thresholdBytes);

    [LoggerMessage(Level = LogLevel.Error, Message = "Recording {RecordingId}: only {FreeBytes} bytes free; stopping before writes fail")]
    private partial void LogStopFloor(string recordingId, long freeBytes);

    [LoggerMessage(Level = LogLevel.Error, Message = "Recording {RecordingId}: recording.state.json could not be written")]
    private partial void LogStateFileFailed(Exception exception, string recordingId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Recording {RecordingId}: project.json could not be updated")]
    private partial void LogManifestWriteFailed(Exception exception, string recordingId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Finalizing recording {RecordingId} failed; its files are kept for the next launch")]
    private partial void LogFinalizeFailed(Exception exception, string recordingId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Recording {RecordingId} is stored, but its later processing stages could not be queued")]
    private partial void LogQueueFailed(Exception exception, string recordingId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The empty project {RecordingId} of a recording that did not start could not be removed")]
    private partial void LogDiscardFailed(Exception exception, string recordingId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The source selection could not be remembered in settings")]
    private partial void LogRememberFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Flushing the recording before the crash exit failed")]
    private partial void LogCrashFlushFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Recording {RecordingId} was stopped by app shutdown at {ElapsedMs} ms; it is finalized at the next launch")]
    private partial void LogStoppedAtShutdown(string recordingId, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Error, Message = "Recording {RecordingId} could not be stopped cleanly at shutdown; recovery repairs it at the next launch")]
    private partial void LogShutdownStopFailed(Exception exception, string recordingId);

    private sealed class ActiveRecording(IRecordingSession session, string recordingId, string title, TimeSpan checkpointInterval)
    {
        public int HighlightsCount;
        public long LastLevelsTimestamp;

        public IRecordingSession Session { get; } = session;

        public string SessionId => Session.SessionId;

        public string RecordingId { get; } = recordingId;

        public string Title { get; } = title;

        public DateTimeOffset StartedAt => Session.StartedAt;

        public TimeSpan CheckpointInterval { get; } = checkpointInterval;

        public CancellationTokenSource Stopping { get; } = new();

        public SemaphoreSlim StateFileGate { get; } = new(1, 1);

        public Task Loops { get; set; } = Task.CompletedTask;

        public DateTimeOffset? LastCheckpointAt { get; set; }

        public string? LostSource { get; set; }

        public bool LowSpaceRaised { get; set; }

        public bool Completed { get; set; }

        public async Task StopLoopsAsync()
        {
            await Stopping.CancelAsync();
            await Loops;
        }
    }

    private sealed class Finalizing(RecordingStatePayload payload)
    {
        public RecordingStatePayload Payload { get; } = payload;

        private volatile bool _finished;

        public Task Task { get; set; } = Task.CompletedTask;

        /// <summary>Set just before the outcome (ready or stopped) is published; <see cref="Current"/> leaves it out.</summary>
        public bool Finished
        {
            get => _finished;
            set => _finished = value;
        }
    }
}
