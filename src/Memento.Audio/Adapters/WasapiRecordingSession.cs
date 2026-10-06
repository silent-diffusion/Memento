using Memento.Audio.Capture;
using Memento.Core.Audio;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Recording;
using Microsoft.Extensions.Logging;
using A = Memento.Audio.Recording;

namespace Memento.Audio.Adapters;

/// <summary>
/// <see cref="IRecordingSession"/> over one <see cref="A.AudioRecordingSession"/>. Translates its events
/// (<c>Levels</c>, <c>Checkpointed</c>, <c>SourceLost</c>, <c>Stopped</c>) into Core's (<see cref="LevelsAvailable"/>,
/// <see cref="CheckpointWritten"/>, <see cref="SourceLost"/>, <see cref="HostStopped"/>) through a
/// <see cref="SessionEventDispatcher"/>, so no Core handler ever runs on an audio thread. Track ids are the audio
/// layer's file stems (<c>mic</c>, <c>system</c>, <c>app-zoom</c>, <c>mic-2</c>) and files are their first WAV part.
/// </summary>
public sealed partial class WasapiRecordingSession : IRecordingSession
{
    private readonly A.AudioRecordingSession _session;
    private readonly SessionEventDispatcher _dispatcher;
    private readonly TimeProvider _time;
    private readonly ILogger<WasapiRecordingSession> _logger;
    private readonly object _sync = new();
    private readonly Dictionary<string, AudioSource> _sources = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _checkpointedBytes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _addedAtMs = new(StringComparer.Ordinal);
    private readonly List<SessionPause> _pauses = [];
    private RecordingSessionState _state = RecordingSessionState.Recording;
    private Task<RecordingSessionResult>? _stop;
    private RecordingSessionResult? _mapped;
    private int _disposed;

    internal WasapiRecordingSession(A.AudioRecordingSession session, RecordingPlan plan, TimeProvider time, ILogger<WasapiRecordingSession> logger)
    {
        _session = session;
        _time = time;
        _logger = logger;
        _dispatcher = new SessionEventDispatcher(logger);
        foreach (var source in plan.Sources)
        {
            _sources[source.Id] = source;
        }

        SessionId = "wasapi-" + Guid.NewGuid().ToString("N")[..12];
        session.Levels += OnLevels;
        session.Checkpointed += OnCheckpointed;
        session.SourceLost += OnSourceLost;
        session.Stopped += OnStopped;
    }

    public event EventHandler<LevelsEventArgs>? LevelsAvailable;

    public event EventHandler<SessionStateChangedEventArgs>? StateChanged;

    public event EventHandler<CheckpointEventArgs>? CheckpointWritten;

    public event EventHandler<SourceLostEventArgs>? SourceLost;

    public event EventHandler<HostStoppedEventArgs>? HostStopped;

    public string SessionId { get; }

    public DateTimeOffset StartedAt => _session.StartedAt;

    public RecordingSessionState State
    {
        get
        {
            lock (_sync)
            {
                return _state;
            }
        }
    }

    public long ElapsedMs => (long)_session.Elapsed.TotalMilliseconds;

    public IReadOnlyList<SessionTrack> Tracks => _session.Tracks.Select(ToSessionTrack).ToList();

    public IReadOnlyList<SessionPause> Pauses
    {
        get
        {
            lock (_sync)
            {
                return [.. _pauses];
            }
        }
    }

    public DateTimeOffset? LastCheckpointAt => _session.LastCheckpointAt;

    public Task PauseAsync(CancellationToken cancellationToken)
    {
        long at;
        lock (_sync)
        {
            if (_state != RecordingSessionState.Recording)
            {
                return Task.CompletedTask;
            }

            _session.Pause();
            at = ElapsedMs;
            _pauses.Add(new SessionPause(at, _time.GetLocalNow(), null));
            _state = RecordingSessionState.Paused;
        }

        _dispatcher.Post(() => StateChanged?.Invoke(this, new SessionStateChangedEventArgs(RecordingSessionState.Paused, at)));

        // A pause is a natural moment to make everything so far durable; capture is not waiting on it.
        _ = CheckpointQuietlyAsync();
        return Task.CompletedTask;
    }

    public Task ResumeAsync(CancellationToken cancellationToken)
    {
        long at;
        lock (_sync)
        {
            if (_state != RecordingSessionState.Paused)
            {
                return Task.CompletedTask;
            }

            var gap = _session.Resume();
            at = ElapsedMs;
            if (gap is not null && _pauses.Count > 0 && _pauses[^1].DurationMs is null)
            {
                _pauses[^1] = _pauses[^1] with { AtMs = (long)gap.At.TotalMilliseconds, DurationMs = (long)gap.Duration.TotalMilliseconds };
            }

            _state = RecordingSessionState.Recording;
        }

        _dispatcher.Post(() => StateChanged?.Invoke(this, new SessionStateChangedEventArgs(RecordingSessionState.Recording, at)));
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<SessionTrack>> SetSourceAsync(AudioSource source, bool enabled, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        lock (_sync)
        {
            if (_state == RecordingSessionState.Stopped)
            {
                throw new InvalidOperationException("The recording has stopped; sources can no longer change.");
            }

            _sources[source.Id] = source;
        }

        var addedAt = ElapsedMs;
        try
        {
            var status = await _session.SetSourceAsync(source.Id, enabled, cancellationToken).ConfigureAwait(false);
            if (enabled)
            {
                lock (_sync)
                {
                    _addedAtMs.TryAdd(status.FileStem, addedAt);
                }

                LogTrackAdded(source.Id, status.FileStem, addedAt);
            }
            else
            {
                LogTrackRemoved(source.Id, status.FileStem, (long)(status.EndedEarlyAt ?? TimeSpan.Zero).TotalMilliseconds);
            }
        }
        catch (AudioSourceUnavailableException ex)
        {
            LogSourceUnavailable(ex, source.Id);
            throw new SourceUnavailableException(source.Id, source.Name, SourceUnavailableReason.From(ex));
        }
        catch (ArgumentException) when (!enabled)
        {
            // Turning off a source that never recorded in this session: nothing to end.
        }

        return Tracks;
    }

    public Task<long> MarkAsync(CancellationToken cancellationToken) => Task.FromResult(ElapsedMs);

    public async Task CheckpointAsync(CancellationToken cancellationToken)
    {
        if (State == RecordingSessionState.Stopped || Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        try
        {
            await _session.CheckpointAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // Stopped in the meantime; stop already made every track durable.
        }
    }

    public Task<RecordingSessionResult> StopAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            _stop ??= StopCoreAsync();
            return _stop;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        await _dispatcher.DrainAsync().ConfigureAwait(false);
        _session.Levels -= OnLevels;
        _session.Checkpointed -= OnCheckpointed;
        _session.SourceLost -= OnSourceLost;
        _session.Stopped -= OnStopped;
        await _session.DisposeAsync().ConfigureAwait(false);
        await _dispatcher.DisposeAsync().ConfigureAwait(false);
    }

    internal static HostStopReason? MapStopReason(A.SessionStopReason reason) => reason switch
    {
        A.SessionStopReason.DiskFull => HostStopReason.DiskFull,
        A.SessionStopReason.AllSourcesLost => HostStopReason.DeviceLost,
        A.SessionStopReason.WriteFailed => HostStopReason.Error,
        _ => null,
    };

    internal static Core.Recording.TrackEndReason? MapEndReason(A.TrackEndReason? reason, bool diskFull) => reason switch
    {
        A.TrackEndReason.SourceLost => Core.Recording.TrackEndReason.SourceLost,
        A.TrackEndReason.Disabled => Core.Recording.TrackEndReason.Disabled,
        A.TrackEndReason.WriteFailed => diskFull ? Core.Recording.TrackEndReason.DiskFull : Core.Recording.TrackEndReason.Error,
        _ => null,
    };

    private static PcmFormat Format(int sampleRate, int channels, int bitsPerSample) => new(sampleRate, channels, bitsPerSample, SampleEncoding.Pcm);

    private static string FileOf(string stem) => $"{Core.Projects.ProjectLayout.TracksFolder}/{Writing.WavTrackSet.PartFileName(stem, 1)}";

    private async Task<RecordingSessionResult> StopCoreAsync()
    {
        var result = await _session.StopAsync(CancellationToken.None).ConfigureAwait(false);
        return Map(result);
    }

    /// <summary>Converts the stop result once; the user's stop and the session's own event both end up here.</summary>
    private RecordingSessionResult Map(A.AudioSessionResult result)
    {
        lock (_sync)
        {
            return _mapped ??= MapLocked(result);
        }
    }

    private RecordingSessionResult MapLocked(A.AudioSessionResult result)
    {
        var diskFull = result.StopReason == A.SessionStopReason.DiskFull;
        var tracks = result.Tracks.Select(t => ToSessionTrack(t, diskFull)).ToList();
        List<SessionPause> pauses;
        lock (_sync)
        {
            _state = RecordingSessionState.Stopped;
            pauses = result.Gaps.Select((gap, i) => new SessionPause(
                (long)gap.At.TotalMilliseconds,
                i < _pauses.Count ? _pauses[i].PausedAt : result.StoppedAt,
                (long)gap.Duration.TotalMilliseconds)).ToList();

            // Stopped while paused: that pause never closed, so it lasts until the stop.
            if (_pauses.Count > pauses.Count && _pauses[^1].DurationMs is null)
            {
                var open = _pauses[^1];
                pauses.Add(open with { DurationMs = (long)Math.Max(0, (result.StoppedAt - open.PausedAt).TotalMilliseconds) });
            }

            _pauses.Clear();
            _pauses.AddRange(pauses);
        }

        return new RecordingSessionResult((long)result.Duration.TotalMilliseconds, tracks, pauses, result.StoppedAt, MapStopReason(result.StopReason));
    }

    private AudioSource SourceOf(string sourceId, A.TrackStatus? status = null, A.TrackResult? result = null)
    {
        lock (_sync)
        {
            if (_sources.TryGetValue(sourceId, out var known))
            {
                return known;
            }
        }

        var kind = status?.Kind ?? result?.Kind ?? AudioSourceKind.Microphone;
        var name = status?.Name ?? result?.Name ?? sourceId;
        int? processId = kind == AudioSourceKind.Application && AudioSourceId.TryParse(sourceId, out var id) ? id.ProcessId : null;
        return new AudioSource(sourceId, WasapiAudioSourceProvider.KindName(kind), name, string.Empty, false, processId);
    }

    private SessionTrack ToSessionTrack(A.TrackStatus status)
    {
        var format = Format(status.SampleRate, status.Channels, status.BitsPerSample);
        long checkpointed;
        long startMs;
        lock (_sync)
        {
            checkpointed = _checkpointedBytes.GetValueOrDefault(status.FileStem);

            // A source turned on mid-session reports offset 0 until its first packet arrives.
            startMs = !status.HasAudio && _addedAtMs.TryGetValue(status.FileStem, out var added) ? added : (long)status.StartOffset.TotalMilliseconds;
        }

        return new SessionTrack(
            status.FileStem,
            SourceOf(status.SourceId, status),
            FileOf(status.FileStem),
            format,
            startMs,
            status.Frames * format.BlockAlign,
            checkpointed,
            status.EndedEarlyAt is { } ended ? (long)ended.TotalMilliseconds : null,
            MapEndReason(status.EndReason, diskFull: false));
    }

    private SessionTrack ToSessionTrack(A.TrackResult result, bool diskFull)
    {
        var format = Format(result.StorageFormat.SampleRate, result.StorageFormat.Channels, result.StorageFormat.BitsPerSample);
        var bytes = result.Frames * format.BlockAlign;
        lock (_sync)
        {
            _checkpointedBytes[result.FileStem] = bytes;
        }

        return new SessionTrack(
            result.FileStem,
            SourceOf(result.SourceId, result: result),
            FileOf(result.FileStem),
            format,
            (long)result.StartOffset.TotalMilliseconds,
            bytes,
            bytes,
            result.EndedEarlyAt is { } ended ? (long)ended.TotalMilliseconds : null,
            MapEndReason(result.EndReason, diskFull));
    }

    private async Task CheckpointQuietlyAsync()
    {
        try
        {
            await CheckpointAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            LogCheckpointFailed(ex);
        }
    }

    private void OnLevels(object? sender, A.LevelsEventArgs e)
    {
        var levels = e.Levels.Select(l => new SourceLevel(l.SourceId, l.Rms, l.Peak)).ToList();
        _dispatcher.PostLevels(() => LevelsAvailable?.Invoke(this, new LevelsEventArgs(levels)));
    }

    private void OnCheckpointed(object? sender, A.CheckpointEventArgs e)
    {
        var checkpoint = e.Checkpoint;
        lock (_sync)
        {
            foreach (var track in checkpoint.Tracks)
            {
                _checkpointedBytes[track.FileStem] = track.BytesWritten;
            }
        }

        var tracks = Tracks;
        var elapsed = (long)checkpoint.Elapsed.TotalMilliseconds;
        _dispatcher.Post(() => CheckpointWritten?.Invoke(this, new CheckpointEventArgs(checkpoint.At, elapsed, tracks)));
    }

    private void OnSourceLost(object? sender, A.SourceLostEventArgs e)
    {
        LogSourceLost(e.SourceId, e.Reason, (long)e.At.TotalMilliseconds, e.Message);
        var tracks = Tracks;
        var lost = tracks.LastOrDefault(t => t.Source.Id == e.SourceId);
        if (lost is null)
        {
            return;
        }

        var remaining = tracks.Where(t => t.IsOpen).ToList();
        var at = (long)e.At.TotalMilliseconds;
        _dispatcher.Post(() => SourceLost?.Invoke(this, new SourceLostEventArgs(lost, at, remaining)));
    }

    private void OnStopped(object? sender, A.SessionStoppedEventArgs e)
    {
        var result = e.Result;
        var reason = MapStopReason(result.StopReason);
        RecordingSessionResult mapped;
        lock (_sync)
        {
            mapped = Map(result);
            _stop ??= Task.FromResult(mapped);
        }

        _dispatcher.Post(() => StateChanged?.Invoke(this, new SessionStateChangedEventArgs(RecordingSessionState.Stopped, mapped.ElapsedMs)));
        if (reason is { } hostReason)
        {
            LogHostStopped(hostReason, mapped.ElapsedMs, result.StopMessage ?? string.Empty);
            _dispatcher.Post(() => HostStopped?.Invoke(this, new HostStoppedEventArgs(hostReason, mapped.ElapsedMs, result.StopMessage ?? result.StopReason.ToString(), mapped)));
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Track {TrackId} for {SourceId} added at {AtMs} ms")]
    private partial void LogTrackAdded(string sourceId, string trackId, long atMs);

    [LoggerMessage(Level = LogLevel.Information, Message = "Track {TrackId} for {SourceId} turned off at {AtMs} ms")]
    private partial void LogTrackRemoved(string sourceId, string trackId, long atMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Source {SourceId} could not be opened mid-session; the other tracks continue")]
    private partial void LogSourceUnavailable(Exception exception, string sourceId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Source {SourceId} lost ({Reason}) at {AtMs} ms: {Message}")]
    private partial void LogSourceLost(string sourceId, CaptureLostReason reason, long atMs, string message);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The audio session stopped by itself ({Reason}) at {AtMs} ms: {Message}")]
    private partial void LogHostStopped(HostStopReason reason, long atMs, string message);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A checkpoint at pause failed; the next timed checkpoint tries again")]
    private partial void LogCheckpointFailed(Exception exception);
}
