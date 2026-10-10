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
    private bool _paused;

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
                Report(new VideoProblemEventArgs(role, role, "notStarted", ex.Code, ex.Message, _session.ElapsedMs));
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
            throw new BridgeException(ex.Code, ex.Message, sourceId);
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
                    entries.Add(flush ? recorder.Checkpoint() : ToCheckpoint(recorder));
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

    public IReadOnlyList<VideoTrackStatus> Statuses()
    {
        lock (_sync)
        {
            return _finished.Select(ToStatus).Concat(_active.Values.Select(SafeStatus).OfType<VideoTrackStatus>()).ToList();
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
            await EndAsync(role, recorder, "diskFull", CancellationToken.None);
            Report(new VideoProblemEventArgs(
                recorder.TrackId,
                role,
                "ended",
                DomainErrorCodes.VideoDiskFull,
                $"Low disk space · {HumanFormat.Bytes(freeBytes)} free. The {role} video stopped at {HumanFormat.Clock(at)} so audio can keep recording; everything before it is saved. Free up space to record video again.",
                at));
        }
    }

    /// <summary>Stops every track and returns them all in the order they started; the files stay playable as written.</summary>
    public async Task<IReadOnlyList<VideoTrack>> StopAllAsync()
    {
        List<(string Role, IVideoTrackRecorder Recorder)> running;
        lock (_sync)
        {
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
        var status = recorder.Status();
        return new VideoTrackCheckpoint(
            recorder.TrackId,
            recorder.Role,
            VideoSourceIds.KindOf(recorder.SourceId) ?? "display",
            recorder.SourceId,
            status.SourceName,
            status.Width,
            status.Height,
            status.FrameRate,
            [new VideoTrackPart($"tracks/{recorder.TrackId}.mp4", status.StartOffsetMs, status.DurationMs, status.Encoder, status.Hardware, status.FramesWritten)],
            status.BytesWritten,
            status.FramesWritten,
            status.FramesDropped,
            null,
            null);
    }

    private static VideoTrackStatus ToStatus(VideoTrack track) =>
        new(
            track.Id,
            track.Role,
            track.EndReason is null or "disabled" ? "stopped" : "ended",
            track.SourceId,
            track.Name,
            null,
            track.Encoder,
            track.HardwareEncoder,
            track.Width,
            track.Height,
            track.FrameRate,
            track.BitrateBitsPerSecond,
            0,
            track.FramesWritten,
            track.FramesDropped,
            0,
            track.DurationMs,
            track.StartOffsetMs);

    private VideoTrackStatus? SafeStatus(IVideoTrackRecorder recorder)
    {
        try
        {
            return recorder.Status();
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

    private async Task StartTrackAsync(string role, string sourceId, CancellationToken cancellationToken)
    {
        var trackId = NextTrackId(role);
        var paused = _session.Pauses.Sum(p => p.DurationMs ?? 0);
        var origin = _originQpc + (paused * VideoClock.TicksPerMillisecond);
        var recorder = await _capture.StartTrackAsync(new VideoTrackRequest(trackId, role, sourceId, _projectFolder, origin, _settings), cancellationToken);
        recorder.Problem += (_, e) => Report(e);
        lock (_sync)
        {
            _active[role] = recorder;
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

    private void Report(VideoProblemEventArgs e)
    {
        LogProblem(_recordingId, e.TrackId, e.Code, e.State, e.AtMs);
        try
        {
            _publisher.PublishRecordingVideoProblem(new RecordingVideoProblemPayload(_session.SessionId, e.TrackId, e.Role, e.State, e.Code, e.Message, e.AtMs));
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Recording {RecordingId}: video track {TrackId} switched to another {Kind}")]
    private partial void LogSwitched(string recordingId, string trackId, string kind);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Recording {RecordingId}: video track {TrackId}: {Code} ({State}) at {AtMs} ms")]
    private partial void LogProblem(string recordingId, string trackId, string code, string state, long atMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Recording {RecordingId}: video track {TrackId} could not be checkpointed")]
    private partial void LogCheckpointFailed(Exception exception, string recordingId, string trackId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Recording {RecordingId}: video track {TrackId} did not stop cleanly; its file is kept for recovery")]
    private partial void LogStopFailed(Exception exception, string recordingId, string trackId);
}
