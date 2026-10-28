using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Formatting;
using Memento.Core.Projects;
using Memento.Core.Settings;
using Memento.Core.Video;
using Microsoft.Extensions.Logging;

namespace Memento.Core.Recording;

/// <summary>
/// The video tracks of one recording session (ARCHITECTURE.md §5.x): one <see cref="IVideoTrackRecorder"/> per role
/// (<c>screen</c>, <c>camera</c>), started on the audio session's timeline, paused, resumed and checkpointed with it,
/// switched or ended by <c>recording.setVideoSource</c>. Video fails first and alone (DESIGN.md §18.6): a track that
/// cannot start or that fails is reported with <c>recording.videoProblem</c> and never stops the audio. Every call here
/// comes from the coordinator (under its gate) or from a track's own event; none runs on a capture thread.
/// </summary>
internal sealed partial class VideoRecording : IAsyncDisposable
{
    private readonly IVideoCapture _capture;
    private readonly IRecordingSession _session;
    private readonly string _recordingId;
    private readonly string _projectFolder;
    private readonly long _originQpc;
    private readonly VideoSettings _settings;
    private readonly BridgeEventPublisher _publisher;
    private readonly ILogger _logger;
    private readonly object _sync = new();
    private readonly Dictionary<string, IVideoTrackRecorder> _active = new(StringComparer.Ordinal);
    private readonly List<VideoTrack> _finished = [];
    private readonly HashSet<string> _usedIds = new(StringComparer.Ordinal);
    private readonly List<ProjectPipChange> _pip = [];
    private readonly Dictionary<string, CancellationTokenSource> _retries = new(StringComparer.Ordinal);
    private bool _paused;
    private bool _stopping;

    public VideoRecording(
        IVideoCapture capture,
        IRecordingSession session,
        string recordingId,
        string projectFolder,
        long originQpc,
        VideoSettings settings,
        BridgeEventPublisher publisher,
        ILogger logger)
    {
        _capture = capture;
        _session = session;
        _recordingId = recordingId;
        _projectFolder = projectFolder;
        _originQpc = originQpc;
        _settings = settings;
        _publisher = publisher;
        _logger = logger;
        Pip = new PipState(settings.DefaultSource == VideoSettings.SourceScreenAndCamera, settings.PipCorner);
    }

    /// <summary>2.8.0: the most screens (displays and windows) recorded at once, the first one included.</summary>
    public const int MaxScreens = 4;

    /// <summary>How often a camera another app holds is tried again (it starts by itself the moment it is free).</summary>
    public TimeSpan CameraRetryInterval { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// 2.1, "Resume recording": video track ids earlier segments used; a new track takes the next free one
    /// (<c>screen-2</c>), so its file never touches theirs.
    /// </summary>
    public IReadOnlyCollection<string> ReservedTrackIds
    {
        get
        {
            lock (_sync)
            {
                return _usedIds.ToList();
            }
        }

        init
        {
            lock (_sync)
            {
                _usedIds.UnionWith(value);
            }
        }
    }

    /// <summary>The inset as the page last set it.</summary>
    public PipState Pip { get; private set; }

    /// <summary>Any video track was started in this session (running or finished).</summary>
    public bool HasAny
    {
        get
        {
            lock (_sync)
            {
                return _active.Count > 0 || _finished.Count > 0;
            }
        }
    }

    /// <summary>Some video track is running (not ended).</summary>
    public bool IsRecording
    {
        get
        {
            lock (_sync)
            {
                return _active.Values.Any(r => r.Status().State is not ("ended" or "stopped"));
            }
        }
    }

    public IReadOnlyList<ProjectPipChange> PipChanges
    {
        get
        {
            lock (_sync)
            {
                return _pip.ToList();
            }
        }
    }

    /// <summary>
    /// Starts the video sources chosen with <c>recording.start</c>. One source per role (the first display or window and the
    /// first camera); one that cannot start is reported and the others go on.
    /// </summary>
    public async Task StartInitialAsync(IReadOnlyList<string> sourceIds, CancellationToken cancellationToken)
    {
        var byRole = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var id in sourceIds)
        {
            if (VideoSourceIds.RoleOf(id) is { } role && !byRole.ContainsKey(role))
            {
                byRole[role] = id;
            }
        }

        foreach (var (role, id) in byRole.OrderBy(kv => kv.Key == VideoTrack.RoleScreen ? 0 : 1))
        {
            try
            {
                await StartTrackAsync(role, id, cancellationToken);
            }
            catch (VideoStartException ex)
            {
                ReportStartFailure(role, id, ex);
            }
        }

        if (byRole.ContainsKey(VideoTrack.RoleCamera))
        {
            RecordPip(Pip);
        }
    }

    /// <summary><c>recording.setVideoSource</c>: starts, switches or (with <c>null</c>) ends the role's track.</summary>
    /// <exception cref="BridgeException">The source could not start (its <c>video.*</c> code); the other tracks are untouched.</exception>
    public async Task<IReadOnlyList<VideoTrackStatus>> SetSourceAsync(string role, string? sourceId, CancellationToken cancellationToken)
    {
        IVideoTrackRecorder? current;
        lock (_sync)
        {
            _active.TryGetValue(role, out current);
        }

        CancelRetry(role);
        if (sourceId is null)
        {
            if (current is not null)
            {
                await EndAsync(role, current, "disabled", cancellationToken);
            }

            return Statuses();
        }

        if (VideoSourceIds.RoleOf(sourceId) != role)
        {
            throw new BridgeException(
                BridgeErrorCodes.InvalidParams,
                role == VideoTrack.RoleCamera
                    ? "The camera track records a camera; choose one from video.listSources' cameras."
                    : "The screen track records a display or a window; choose one from video.listSources' displays or windows.",
                sourceId);
        }

        try
        {
            if (current is not null && current.Status().State is not ("ended" or "stopped"))
            {
                // "You can change this while recording; the track continues" (DESIGN.md §18.2).
                if (!string.Equals(current.SourceId, sourceId, StringComparison.Ordinal))
                {
                    await current.SwitchSourceAsync(sourceId, cancellationToken);
                    var kind = VideoSourceIds.KindOf(sourceId) ?? "video";
                    LogSwitched(_recordingId, current.TrackId, kind);
                }

                return Statuses();
            }

            if (current is not null)
            {
                // The track ended (window closed, camera unplugged): a new one starts from here.
                await EndAsync(role, current, null, cancellationToken);
            }

            await StartTrackAsync(role, sourceId, cancellationToken);
            if (role == VideoTrack.RoleCamera)
            {
                RecordPip(Pip);
            }
        }
        catch (VideoStartException ex)
        {
            if (ex.Code == VideoProblemCodes.CameraInUse)
            {
                ReportStartFailure(role, sourceId, ex);
            }

            throw new BridgeException(ex.Code, ex.Message, sourceId);
        }

        return Statuses();
    }

    /// <summary>
    /// <c>video.addSource</c> (2.8.0): starts recording one more display or window from now, beside the tracks already
    /// running. Without a running screen track it becomes the screen track; otherwise it is a track of its own
    /// (<c>screen-2</c>, ...), listed by that id. Audio and the other tracks are untouched.
    /// </summary>
    /// <exception cref="BridgeException">A camera, a source already recorded, one screen too many, or its <c>video.*</c> start code.</exception>
    public async Task<IReadOnlyList<VideoTrackStatus>> AddSourceAsync(string sourceId, CancellationToken cancellationToken)
    {
        if (VideoSourceIds.RoleOf(sourceId) != VideoTrack.RoleScreen)
        {
            throw new BridgeException(
                BridgeErrorCodes.InvalidParams,
                "Add screen records a display or a window; choose one from video.listSources' displays or windows. A camera is set with recording.setVideoSource.",
                sourceId);
        }

        IVideoTrackRecorder? primary;
        int runningCount;
        bool already;
        lock (_sync)
        {
            var running = _active.Where(kv => IsScreenSlot(kv.Key) && IsRunning(kv.Value)).ToList();
            already = running.Any(kv => string.Equals(kv.Value.SourceId, sourceId, StringComparison.Ordinal));
            runningCount = running.Count;
            _active.TryGetValue(VideoTrack.RoleScreen, out primary);
        }

        if (already)
        {
            throw new BridgeException(
                DomainErrorCodes.VideoSourceAlreadyRecorded,
                $"{NameOf(sourceId)} is already being recorded. Nothing changed; choose another display or window.",
                sourceId);
        }

        if (runningCount >= MaxScreens)
        {
            throw new BridgeException(
                DomainErrorCodes.VideoTooManySources,
                $"Memento records up to {MaxScreens} screens at once and {runningCount} are recording. Nothing changed; remove one to add {NameOf(sourceId)}.",
                sourceId);
        }

        try
        {
            if (primary is null || !IsRunning(primary))
            {
                if (primary is not null)
                {
                    // The screen track ended (window closed): the added source takes its place from now.
                    await EndAsync(VideoTrack.RoleScreen, primary, null, cancellationToken);
                }

                await StartTrackAsync(VideoTrack.RoleScreen, sourceId, cancellationToken);
            }
            else
            {
                await StartTrackAsync(VideoTrack.RoleScreen, sourceId, cancellationToken, extra: true);
            }
        }
        catch (VideoStartException ex)
        {
            throw new BridgeException(ex.Code, ex.Message, sourceId);
        }

        return Statuses();
    }

    /// <summary>
    /// <c>video.removeSource</c> (2.8.0): stops one track (<c>screen</c>, <c>screen-2</c>, <c>camera</c>) from now; its
    /// file keeps what it recorded. Removing the last screen is allowed: audio keeps recording.
    /// </summary>
    /// <exception cref="BridgeException"><c>video.sourceNotFound</c> when no track of the session has that id.</exception>
    public async Task<IReadOnlyList<VideoTrackStatus>> RemoveSourceAsync(string trackId, CancellationToken cancellationToken)
    {
        IVideoTrackRecorder? recorder;
        lock (_sync)
        {
            _active.TryGetValue(trackId, out recorder);
        }

        CancelRetry(trackId);
        if (recorder is null)
        {
            throw new BridgeException(
                DomainErrorCodes.VideoSourceNotFound,
                $"There is no video track '{trackId}' in this recording any more. Nothing changed; audio and the other tracks keep recording.",
                trackId);
        }

        await EndAsync(trackId, recorder, "disabled", cancellationToken);
        return Statuses();
    }

    /// <summary><c>recording.setVideoQuality</c>: the running tracks continue at the new quality (§18.6 "Lower quality").</summary>
    public async Task<IReadOnlyList<VideoTrackStatus>> SetQualityAsync(string quality, CancellationToken cancellationToken)
    {
        if (!VideoSettings.Qualities.Contains(quality, StringComparer.Ordinal))
        {
            throw new BridgeException(BridgeErrorCodes.InvalidParams, $"Video quality is one of {string.Join(", ", VideoSettings.Qualities)}, not '{quality}'.", quality);
        }

        List<IVideoTrackRecorder> running;
        lock (_sync)
        {
            running = _active.Where(kv => SafeStatus(kv.Key, kv.Value)?.State is not ("ended" or "stopped")).Select(kv => kv.Value).ToList();
        }

        foreach (var recorder in running)
        {
            try
            {
                await recorder.SetQualityAsync(quality, cancellationToken);
            }
            catch (VideoStartException ex)
            {
                throw new BridgeException(ex.Code, ex.Message, recorder.SourceId);
            }
        }

        return Statuses();
    }

    /// <summary><c>recording.setPip</c>: the inset's state from now on (recorded for the export's "One file").</summary>
    public void SetPip(bool enabled, string? corner)
    {
        if (corner is not null && !VideoPipChange.IsCorner(corner))
        {
            throw new BridgeException(BridgeErrorCodes.InvalidParams, $"Inset corner '{corner}' is not one of {string.Join(", ", VideoPipChange.Corners)}.", corner);
        }

        var next = new PipState(enabled, corner ?? Pip.Corner);
        Pip = next;
        bool camera;
        lock (_sync)
        {
            camera = _active.ContainsKey(VideoTrack.RoleCamera) || _finished.Any(t => t.Role == VideoTrack.RoleCamera);
        }

        if (camera)
        {
            RecordPip(next);
        }
    }

    public void Pause()
    {
        lock (_sync)
        {
            _paused = true;
            foreach (var recorder in _active.Values)
            {
                recorder.PauseTrack();
            }
        }
    }

    public void Resume()
    {
        lock (_sync)
        {
            _paused = false;
            foreach (var recorder in _active.Values)
            {
                recorder.ResumeTrack();
            }
        }
    }

    /// <summary>Flushes every running track (when <paramref name="flush"/>) and returns every track's entry for <c>recording.state.json</c>.</summary>
    public IReadOnlyList<VideoTrackCheckpoint> Checkpoint(bool flush)
    {
        lock (_sync)
        {
            var entries = _finished.Select(ToCheckpoint).ToList();
            foreach (var recorder in _active.Values)
            {
                try
                {
                    entries.Add(recorder.Checkpoint(flush));
                }
#pragma warning disable CA1031 // A video flush must never stop the audio checkpoint; the next one tries again.
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    LogCheckpointFailed(ex, _recordingId, recorder.TrackId);
                }
            }

            return entries;
        }
    }

    /// <summary>The running tracks (an ended one stays until it is replaced or the session ends), screen first; a track turned off is left out.</summary>
    public IReadOnlyList<VideoTrackStatus> Statuses()
    {
        lock (_sync)
        {
            return _active
                .OrderBy(kv => kv.Key == VideoTrack.RoleScreen ? 0 : kv.Key == VideoTrack.RoleCamera ? 2 : 1)
                .ThenBy(kv => kv.Value.TrackId, StringComparer.Ordinal)
                .Select(kv => SafeStatus(kv.Key, kv.Value))
                .OfType<VideoTrackStatus>()
                .ToList();
        }
    }

    /// <summary>The disk is nearly full: video stops first so audio can keep recording (DESIGN.md §18.6).</summary>
    public async Task StopForLowDiskAsync(long freeBytes)
    {
        List<(string Role, IVideoTrackRecorder Recorder)> running;
        lock (_sync)
        {
            running = _active.Where(kv => kv.Value.Status().State is not ("ended" or "stopped")).Select(kv => (kv.Key, kv.Value)).ToList();
        }

        foreach (var (role, recorder) in running)
        {
            var at = _session.ElapsedMs;
            var name = SafeStatus(role, recorder)?.SourceName ?? role;
            await EndAsync(role, recorder, "diskFull", CancellationToken.None);
            lock (_sync)
            {
                // A track stopped for space is over for this session: it is not listed as running any more.
                _active.Remove(role);
            }

            Report(new VideoProblemEventArgs(
                recorder.TrackId,
                role,
                VideoProblemCodes.StateOf(VideoProblemCodes.DiskFull),
                VideoProblemCodes.DiskFull,
                recorder.SourceId,
                name,
                null,
                $"Low disk space · {HumanFormat.Bytes(freeBytes)} free. The {recorder.Role} video stopped at {HumanFormat.Clock(at)} so audio can keep recording; everything before it is saved. Free up space to record video again.",
                at));
        }
    }

    /// <summary>Stops every track and returns them all in the order they started; the files stay playable as written.</summary>
    public async Task<IReadOnlyList<VideoTrack>> StopAllAsync()
    {
        List<(string Role, IVideoTrackRecorder Recorder)> running;
        lock (_sync)
        {
            _stopping = true;
            foreach (var retry in _retries.Values)
            {
                retry.Cancel();
            }

            _retries.Clear();
            running = _active.Select(kv => (kv.Key, kv.Value)).ToList();
        }

        foreach (var (role, recorder) in running)
        {
            await EndAsync(role, recorder, null, CancellationToken.None);
        }

        lock (_sync)
        {
            return _finished.OrderBy(t => t.StartOffsetMs).ThenBy(t => t.Id, StringComparer.Ordinal).ToList();
        }
    }

    public async ValueTask DisposeAsync()
    {
        List<IVideoTrackRecorder> left;
        lock (_sync)
        {
            left = _active.Values.ToList();
            _active.Clear();
        }

        foreach (var recorder in left)
        {
            try
            {
                await recorder.DisposeAsync();
            }
#pragma warning disable CA1031 // Disposal after a stop: nothing more to save.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogStopFailed(ex, _recordingId, recorder.TrackId);
            }
        }
    }

    private static VideoTrackCheckpoint ToCheckpoint(VideoTrack track) =>
        new(track.Id, track.Role, track.SourceKind, track.SourceId, track.Name, track.Width, track.Height, track.FrameRate, track.Parts, 0, track.FramesWritten, track.FramesDropped, track.EndedEarlyAtMs, track.EndReason);

    private static VideoTrackCheckpoint ToCheckpoint(IVideoTrackRecorder recorder)
    {
        try
        {
            return recorder.Checkpoint(flush: false);
        }
#pragma warning disable CA1031 // The track failed: describe what is known so its file is not forgotten.
        catch (Exception)
#pragma warning restore CA1031
        {
            return new VideoTrackCheckpoint(
                recorder.TrackId,
                recorder.Role,
                VideoSourceIds.KindOf(recorder.SourceId) ?? "display",
                recorder.SourceId,
                recorder.SourceId,
                0,
                0,
                0,
                [new VideoTrackPart($"tracks/{recorder.TrackId}.mp4", 0, 0, string.Empty, false, 0)],
                0,
                0,
                0,
                null,
                null);
        }
    }

    private static bool IsScreenSlot(string slot) => slot != VideoTrack.RoleCamera;

    private static bool IsRunning(IVideoTrackRecorder recorder)
    {
        try
        {
            return recorder.Status().State is not ("ended" or "stopped");
        }
#pragma warning disable CA1031 // A track whose status cannot be read is not counted as running.
        catch (Exception)
#pragma warning restore CA1031
        {
            return false;
        }
    }

    /// <summary>The status with the id the page uses: the role for the first screen and the camera, the track id for an added screen.</summary>
    private VideoTrackStatus? SafeStatus(string slot, IVideoTrackRecorder recorder)
    {
        try
        {
            return recorder.Status() with { TrackId = slot, Role = recorder.Role };
        }
#pragma warning disable CA1031 // A status read must never break the recording state.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogCheckpointFailed(ex, _recordingId, recorder.TrackId);
            return null;
        }
    }

    private string NextTrackId(string role)
    {
        lock (_sync)
        {
            var id = role;
            for (var n = 2; _usedIds.Contains(id); n++)
            {
                id = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{role}-{n}");
            }

            _usedIds.Add(id);
            return id;
        }
    }

    private async Task StartTrackAsync(string role, string sourceId, CancellationToken cancellationToken, bool extra = false)
    {
        var trackId = NextTrackId(role);
        var slot = extra ? trackId : role;
        var paused = _session.Pauses.Sum(p => p.DurationMs ?? 0);
        var origin = _originQpc + (paused * VideoClock.TicksPerMillisecond);
        IVideoTrackRecorder recorder;
        try
        {
            recorder = await _capture.StartTrackAsync(new VideoTrackRequest(trackId, role, sourceId, _projectFolder, origin, _settings), cancellationToken);
        }
        catch
        {
            lock (_sync)
            {
                // Nothing was written under this id: the next try may use it.
                _usedIds.Remove(trackId);
            }

            throw;
        }

        recorder.Problem += (_, e) => Report(e, slot);
        lock (_sync)
        {
            _active[slot] = recorder;
            if (_paused)
            {
                recorder.PauseTrack();
            }
        }

        var kind = VideoSourceIds.KindOf(sourceId) ?? "video";
        LogStarted(_recordingId, trackId, kind);
    }

    private async Task EndAsync(string role, IVideoTrackRecorder recorder, string? reason, CancellationToken cancellationToken)
    {
        var at = _session.ElapsedMs;
        VideoTrack track;
        try
        {
            track = await recorder.StopAsync(cancellationToken);
        }
#pragma warning disable CA1031 // A track that cannot stop cleanly keeps its file (fragmented, playable as written); recovery repairs it.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogStopFailed(ex, _recordingId, recorder.TrackId);
            var checkpoint = ToCheckpoint(recorder);
            track = new VideoTrack
            {
                Id = checkpoint.TrackId,
                Role = checkpoint.Role,
                SourceKind = checkpoint.SourceKind,
                SourceId = checkpoint.SourceId,
                Name = checkpoint.Name,
                File = checkpoint.Parts[0].File,
                Parts = checkpoint.Parts,
                Width = checkpoint.Width,
                Height = checkpoint.Height,
                FrameRate = checkpoint.FrameRate,
                StartOffsetMs = checkpoint.Parts[0].StartOffsetMs,
                DurationMs = checkpoint.Parts[0].DurationMs,
                FramesWritten = checkpoint.FramesWritten,
                FramesDropped = checkpoint.FramesDropped,
                EndedEarlyAtMs = at,
                EndReason = "error",
            };
        }

        if (reason is not null && track.EndReason is null)
        {
            track = track with { EndedEarlyAtMs = at, EndReason = reason };
        }

        track = track with { StoppedAtMs = track.StoppedAtMs ?? StoppedAt(track) };

        lock (_sync)
        {
            if (_active.TryGetValue(role, out var still) && ReferenceEquals(still, recorder))
            {
                _active.Remove(role);
            }

            _finished.Add(track);
        }

        await recorder.DisposeAsync();
    }

    /// <summary>2.8.0: where a track stopped on the session's timeline: where it ended early, else the end of its frames.</summary>
    internal static long StoppedAt(VideoTrack track) =>
        track.EndedEarlyAtMs ?? Math.Max(track.StartOffsetMs, track.StartOffsetMs + track.DurationMs);

    /// <summary>A source chosen for a role could not start: say why; a camera another app holds is tried again until it is free.</summary>
    private void ReportStartFailure(string role, string sourceId, VideoStartException ex)
    {
        var name = NameOf(sourceId);
        Report(new VideoProblemEventArgs(role, role, VideoProblemCodes.StateOf(ex.Code), ex.Code, sourceId, name, ex.Detail, ex.Message, _session.ElapsedMs));
        if (ex.Code == VideoProblemCodes.CameraInUse && role == VideoTrack.RoleCamera)
        {
            StartRetry(role, sourceId);
        }
    }

    private string NameOf(string sourceId)
    {
        try
        {
            var sources = _capture.ListSources();
            return sources.Displays.Concat(sources.Windows).Concat(sources.Cameras).FirstOrDefault(s => s.Id == sourceId)?.Name ?? sourceId;
        }
#pragma warning disable CA1031 // Only a name for a message.
        catch (Exception)
#pragma warning restore CA1031
        {
            return sourceId;
        }
    }

    private void StartRetry(string role, string sourceId)
    {
        CancellationTokenSource cancel;
        lock (_sync)
        {
            if (_stopping || _retries.ContainsKey(role))
            {
                return;
            }

            cancel = new CancellationTokenSource();
            _retries[role] = cancel;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    await Task.Delay(CameraRetryInterval, cancel.Token);
                    try
                    {
                        await StartTrackAsync(role, sourceId, cancel.Token);
                        LogCameraFree(_recordingId, role);
                        return;
                    }
                    catch (VideoStartException ex) when (ex.Code == VideoProblemCodes.CameraInUse)
                    {
                        // Still held by the other app.
                    }
                    catch (VideoStartException ex)
                    {
                        Report(new VideoProblemEventArgs(role, role, VideoProblemCodes.StateOf(ex.Code), ex.Code, sourceId, NameOf(sourceId), ex.Detail, ex.Message, _session.ElapsedMs));
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // The role was changed, or the session ended.
            }
            finally
            {
                lock (_sync)
                {
                    if (_retries.TryGetValue(role, out var current) && ReferenceEquals(current, cancel))
                    {
                        _retries.Remove(role);
                    }
                }

                cancel.Dispose();
            }
        });
    }

    private void CancelRetry(string role)
    {
        lock (_sync)
        {
            if (_retries.Remove(role, out var retry))
            {
                retry.Cancel();
            }
        }
    }

    private void RecordPip(PipState state)
    {
        var change = new ProjectPipChange(_session.ElapsedMs, state.Enabled, state.Corner);
        lock (_sync)
        {
            if (_pip.Count > 0 && _pip[^1].Enabled == change.Enabled && _pip[^1].Corner == change.Corner)
            {
                return;
            }

            _pip.Add(change);
        }
    }

    private void Report(VideoProblemEventArgs e, string? slot = null)
    {
        LogProblem(_recordingId, e.TrackId, e.Code, e.State, e.AtMs);
        try
        {
            _publisher.PublishRecordingVideoProblem(new RecordingVideoProblemPayload(_session.SessionId, slot ?? e.Role, e.State, e.Code, e.SourceId, e.SourceName, e.Detail, e.Message, e.AtMs));
        }
#pragma warning disable CA1031 // The page may be gone; the problem is logged.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogCheckpointFailed(ex, _recordingId, e.TrackId);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Recording {RecordingId}: video track {TrackId} started ({Kind})")]
    private partial void LogStarted(string recordingId, string trackId, string kind);

    [LoggerMessage(Level = LogLevel.Information, Message = "Recording {RecordingId}: the {Role} camera became free; its track started")]
    private partial void LogCameraFree(string recordingId, string role);

    [LoggerMessage(Level = LogLevel.Information, Message = "Recording {RecordingId}: video track {TrackId} switched to another {Kind}")]
    private partial void LogSwitched(string recordingId, string trackId, string kind);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Recording {RecordingId}: video track {TrackId}: {Code} ({State}) at {AtMs} ms")]
    private partial void LogProblem(string recordingId, string trackId, string code, string state, long atMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Recording {RecordingId}: video track {TrackId} could not be checkpointed")]
    private partial void LogCheckpointFailed(Exception exception, string recordingId, string trackId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Recording {RecordingId}: video track {TrackId} did not stop cleanly; its file is kept for recovery")]
    private partial void LogStopFailed(Exception exception, string recordingId, string trackId);
}
