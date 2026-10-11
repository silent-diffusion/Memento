using Memento.Audio;
using Memento.Video.Encoding;
using Memento.Video.Sources;
using Memento.Core.Projects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Memento.Video.Recording;

/// <summary>
/// Records one video track (ARCHITECTURE.md §5.x): a source, a pacing thread and a writer. The source keeps only its
/// latest picture; the pacing thread (above normal priority, below the audio threads' MMCSS priority) takes it at every
/// frame slot and hands it to the encoder, which refuses rather than waits when it is behind. Nothing here touches the
/// audio capture threads, and no call into this class waits on capture or encoding for longer than a lock held around
/// one frame. Video fails first and alone (DESIGN.md §18.6): a lost source, a failed encoder or a full disk end or pause
/// this track and raise <see cref="Problem"/>; it never throws into the recording.
/// </summary>
public sealed partial class VideoCaptureSession : IAsyncDisposable
{
    private readonly VideoSessionOptions _options;
    private readonly IVideoTrackWriterFactory _writers;
    private readonly ILogger _logger;
    private readonly object _gate = new();
    private readonly ManualResetEvent _stop = new(false);
    private readonly List<PartInfo> _parts = [];
    private readonly FramePacer _pacer;
    private VideoEncoderSettings _settings;
    private IVideoSource _source;
    private IVideoTrackWriter? _writer;
    private Thread? _thread;
    private VideoSessionState _state = VideoSessionState.Created;
    private VideoCaptureException? _error;
    private string? _reason;
    private long? _endedAtMs;
    private string? _endReason;
    private bool _recordingPaused;
    private bool _stopped;

    public VideoCaptureSession(VideoSessionOptions options, IVideoSource source, IVideoTrackWriterFactory writers, ILogger<VideoCaptureSession>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(writers);
        _options = options;
        _source = source;
        _writers = writers;
        _settings = options.Settings;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
        var firstSlot = FramePacer.SlotAt(options.TimelineOriginQpc, options.Settings.FrameRate, QpcClock.Now);
        _pacer = new FramePacer(options.TimelineOriginQpc, options.Settings.FrameRate, firstSlot);
    }

    public string TrackId => _options.TrackId;

    /// <summary>The source captured now.</summary>
    internal IVideoSource Source
    {
        get
        {
            lock (_gate)
            {
                return _source;
            }
        }
    }

    /// <summary>Raised (on a thread-pool or the pacing thread) when the user must be told something about this track.</summary>
    public event EventHandler<VideoSessionEventArgs>? Problem;

    public VideoSessionState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    /// <summary>Starts the source and opens the first file. Throws <see cref="VideoCaptureException"/> when either cannot start.</summary>
    public Task StartAsync(CancellationToken cancellationToken) => StartAsync(startPacingThread: true, cancellationToken);

    internal async Task StartAsync(bool startPacingThread, CancellationToken cancellationToken)
    {
        if (_state != VideoSessionState.Created)
        {
            throw new InvalidOperationException("The video session has already been started.");
        }

        Directory.CreateDirectory(Path.Combine(_options.ProjectFolder, "tracks"));
        Attach(_source);
        await _source.StartAsync(cancellationToken).ConfigureAwait(false);
        OpenPart(_settings);
        _state = VideoSessionState.Recording;
        if (startPacingThread)
        {
            _thread = new Thread(PacingLoop) { Name = $"Memento video {_options.TrackId}", IsBackground = true, Priority = ThreadPriority.AboveNormal };
            _thread.Start();
        }
    }

    private void OpenPart(VideoEncoderSettings settings)
    {
        var relative = _options.RelativePartPath(_parts.Count + 1);
        var writer = _writers.Create(Path.Combine(_options.ProjectFolder, relative), settings);
        _parts.Add(new PartInfo(relative, writer.Encoder));
        _writer = writer;
        LogPart(_options.TrackId, _parts.Count, writer.Encoder.Name, writer.Encoder.IsHardware, settings.Width, settings.Height, settings.FrameRate);
    }

    private void PacingLoop()
    {
        using var waiter = new HighResolutionWaiter();
        while (!_stop.WaitOne(0))
        {
            waiter.WaitUntil(_pacer.NextDueQpc, _stop);
            if (_stop.WaitOne(0))
            {
                break;
            }

            TickOnce(QpcClock.Now);
        }
    }

    /// <summary>One pass of the pacing loop: writes the slot due at <paramref name="nowQpc"/> (tests call it directly).</summary>
    internal void TickOnce(long nowQpc)
    {
        lock (_gate)
        {
            if (_writer is null || _state is VideoSessionState.Ended or VideoSessionState.Stopped)
            {
                return;
            }

            var waiting = _state == VideoSessionState.Waiting || _source.State is VideoSourceState.Paused or VideoSourceState.Lost;
            var result = _pacer.Tick(nowQpc, _source, _writer, paused: _recordingPaused || waiting);
            if (result == VideoWriteResult.Written)
            {
                var part = _parts[^1];
                part.FirstTicks ??= _pacer.LastWrittenTicks;
                part.LastTicks = _pacer.LastWrittenTicks;
                part.Frames++;
            }
            else if (result == VideoWriteResult.Failed)
            {
                HandleWriterFailure(_writer.Statistics.Failure ?? new VideoCaptureException(VideoErrorCode.WriteFailed, VideoErrors.WriteFailed(_options.TrackId, "unknown error")));
            }
        }
    }

    private void HandleWriterFailure(VideoCaptureException failure)
    {
        var atMs = PositionMs();
        var failed = _writer!;
        _ = failed.FinalizeAsync(CancellationToken.None).ContinueWith(_ => failed.DisposeAsync().AsTask(), TaskScheduler.Default);
        _writer = null;
        if (failure.Code == VideoErrorCode.EncoderFailed && _settings.PreferHardware && failed.Encoder.IsHardware)
        {
            try
            {
                OpenPart(_settings with { PreferHardware = false });
                Raise(VideoSessionState.Recording, new VideoCaptureException(VideoErrorCode.EncoderFailed, VideoErrors.HardwareEncoderFailed(atMs), failure), atMs);
                return;
            }
            catch (VideoCaptureException ex)
            {
                failure = ex;
            }
        }

        End(failure, atMs, failure.Code == VideoErrorCode.DiskFull ? "diskFull" : "error");
    }

    private void End(VideoCaptureException error, long atMs, string reason)
    {
        _state = VideoSessionState.Ended;
        _error = error;
        _reason = error.Message;
        _endedAtMs = atMs;
        _endReason = reason;
        Raise(VideoSessionState.Ended, error, atMs);
    }

    private void Raise(VideoSessionState state, VideoCaptureException error, long atMs)
    {
        LogProblem(_options.TrackId, error.Code, atMs);
        var handler = Problem;
        if (handler is not null)
        {
            var args = new VideoSessionEventArgs(_options.TrackId, state, error, atMs);
            _ = Task.Run(() => handler(this, args));
        }
    }

    private long PositionMs() => Math.Max(0, (QpcClock.Now - _pacer.OriginQpc) / QpcClock.TicksPerMillisecond);

    private void Attach(IVideoSource source)
    {
        source.Lost += OnSourceLost;
        source.StateChanged += OnSourceStateChanged;
    }

    private void Detach(IVideoSource source)
    {
        source.Lost -= OnSourceLost;
        source.StateChanged -= OnSourceStateChanged;
    }

    private void OnSourceLost(object? sender, VideoSourceLostEventArgs e)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(sender, _source) || _state is VideoSessionState.Ended or VideoSessionState.Stopped)
            {
                return;
            }

            var atMs = Math.Max(0, (e.AtQpc - _pacer.OriginQpc) / QpcClock.TicksPerMillisecond);
            var error = Reword(e.Error, atMs);
            if (e.Source.Kind == VideoSourceKind.Display && e.Error.Code == VideoErrorCode.SourceLost)
            {
                // A display can come back or be swapped for another (SwitchSourceAsync): hold the last picture and wait.
                _state = VideoSessionState.Waiting;
                _reason = error.Message;
                _error = error;
                Raise(VideoSessionState.Waiting, error, atMs);
                return;
            }

            End(error, atMs, e.Error.Code == VideoErrorCode.ProtectedContent ? "protectedContent" : "sourceLost");
        }
    }

    private VideoCaptureException Reword(VideoCaptureException error, long atMs)
    {
        var name = _source.Name;
        if (error.AtPosition is { } atPosition)
        {
            return new VideoCaptureException(error.Code, atPosition(atMs), error) { Detail = error.Detail };
        }

        return (error.Code, _source.Id.Kind) switch
        {
            (VideoErrorCode.SourceLost, VideoSourceKind.Display) => new VideoCaptureException(error.Code, VideoErrors.DisplayDisconnected(name, atMs), error),
            (VideoErrorCode.SourceLost, VideoSourceKind.Window) => new VideoCaptureException(error.Code, VideoErrors.WindowClosed(name, atMs), error),
            (VideoErrorCode.SourceLost, VideoSourceKind.Camera) => new VideoCaptureException(error.Code, VideoErrors.CameraDisconnected(name, atMs), error),
            _ => error,
        };
    }

    private void OnSourceStateChanged(object? sender, VideoSourceStateChangedEventArgs e)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(sender, _source) || _state is VideoSessionState.Ended or VideoSessionState.Stopped or VideoSessionState.Paused)
            {
                return;
            }

            var wasWaiting = _state == VideoSessionState.Waiting;
            _state = e.State == VideoSourceState.Paused ? VideoSessionState.Waiting : VideoSessionState.Recording;
            _reason = e.Reason;
            if (_state == VideoSessionState.Waiting && !wasWaiting)
            {
                // A minimised window, or a camera another app took: the track waits and continues by itself.
                var atMs = Math.Max(0, (e.AtQpc - _pacer.OriginQpc) / QpcClock.TicksPerMillisecond);
                var error = _source.Id.Kind == VideoSourceKind.Camera
                    ? new VideoCaptureException(VideoErrorCode.CameraInUse, VideoErrors.CameraInUse(_source.Name, null))
                    : new VideoCaptureException(VideoErrorCode.SourceMinimised, VideoErrors.WindowMinimised(_source.Name, atMs));
                _reason = error.Message;
                Raise(VideoSessionState.Waiting, error, atMs);
            }
        }
    }

    /// <summary>
    /// Continues the track at another quality (the §18.6 banner's Lower quality): the current file is closed and the
    /// track goes on in a new part with the new bitrate, so nothing recorded is touched. Throws when no encoder opens.
    /// </summary>
    public void ChangeQuality(VideoQuality quality)
    {
        lock (_gate)
        {
            if (_writer is null || _state is VideoSessionState.Ended or VideoSessionState.Stopped || _settings.Quality == quality)
            {
                return;
            }

            var old = _writer;
            _writer = null;
            _ = old.FinalizeAsync(CancellationToken.None).ContinueWith(_ => old.DisposeAsync().AsTask(), TaskScheduler.Default);
            var next = _settings with { Quality = quality };
            try
            {
                OpenPart(next);
                _settings = next;
            }
            catch (VideoCaptureException ex)
            {
                End(ex, PositionMs(), "error");
                throw;
            }
        }
    }

    /// <summary>The recording paused: the video timeline stops with the audio tracks.</summary>
    public void Pause()
    {
        lock (_gate)
        {
            if (_state is VideoSessionState.Recording or VideoSessionState.Waiting)
            {
                _recordingPaused = true;
                _state = VideoSessionState.Paused;
            }
        }
    }

    /// <summary>The recording resumed: the next frame follows the last one, leaving the paused time out.</summary>
    public void Resume()
    {
        lock (_gate)
        {
            if (_state != VideoSessionState.Paused)
            {
                return;
            }

            _recordingPaused = false;
            _pacer.Rebase(QpcClock.Now);
            _state = _source.State == VideoSourceState.Paused ? VideoSessionState.Waiting : VideoSessionState.Recording;
        }
    }

    /// <summary>
    /// Captures another display or window into the same track ("You can change this while recording; the track
    /// continues", DESIGN.md §18.2). The track keeps its size; the new picture is letterboxed into it.
    /// </summary>
    public async Task SwitchSourceAsync(IVideoSource source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        Attach(source);
        try
        {
            await source.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Detach(source);
            throw;
        }

        IVideoSource old;
        lock (_gate)
        {
            old = _source;
            _source = source;
            Detach(old);
            if (_state == VideoSessionState.Waiting)
            {
                _state = VideoSessionState.Recording;
                _reason = null;
                _error = null;
            }
        }

        await old.StopAsync().ConfigureAwait(false);
        await old.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>Flushes the file to disk (unless <paramref name="flush"/> is false) and returns the track's entry for <c>recording.state.json</c>.</summary>
    public VideoTrackCheckpoint Checkpoint(bool flush = true)
    {
        lock (_gate)
        {
            if (flush)
            {
                _writer?.Checkpoint();
            }

            return new VideoTrackCheckpoint(
                _options.TrackId,
                _options.Role,
                SourceKindName(_source.Id.Kind),
                _source.Id.ToString(),
                _source.Name,
                _settings.Width,
                _settings.Height,
                _settings.FrameRate,
                Parts(),
                _writer?.Statistics.BytesWritten ?? 0,
                _pacer.FramesWritten,
                _pacer.FramesDropped,
                _endedAtMs,
                _endReason);
        }
    }

    public VideoSessionStatus Status()
    {
        lock (_gate)
        {
            var bytes = _writer?.Statistics.BytesWritten ?? 0;
            var durationMs = DurationMs();
            var encoder = _parts.Count == 0 ? null : _parts[^1].Encoder;
            return new VideoSessionStatus(
                _options.TrackId,
                _state,
                _source.Name,
                _reason,
                _error,
                encoder?.Name ?? string.Empty,
                encoder?.IsHardware ?? false,
                _settings.Width,
                _settings.Height,
                _settings.FrameRate,
                _settings.BitrateBitsPerSecond,
                durationMs <= 0 ? 0 : bytes * 8 * 1000 / durationMs,
                _pacer.FramesWritten,
                _pacer.FramesDropped,
                bytes + PreviousPartsBytes(),
                durationMs,
                _source.Id.ToString(),
                (_parts.Select(p => p.FirstTicks).FirstOrDefault(t => t is not null) ?? 0) / QpcClock.TicksPerMillisecond);
        }
    }

    private long PreviousPartsBytes()
    {
        long total = 0;
        for (var i = 0; i < _parts.Count - 1; i++)
        {
            var path = Path.Combine(_options.ProjectFolder, _parts[i].RelativePath);
            total += File.Exists(path) ? new FileInfo(path).Length : 0;
        }

        return total;
    }

    private long DurationMs()
    {
        var first = _parts.Select(p => p.FirstTicks).FirstOrDefault(t => t is not null);
        var last = _parts.Select(p => p.LastTicks).LastOrDefault(t => t is not null);
        return first is null || last is null ? 0 : (last.Value + _pacer.PeriodTicks - first.Value) / QpcClock.TicksPerMillisecond;
    }

    private List<VideoTrackPart> Parts() =>
        _parts.Select(p => new VideoTrackPart(
            p.RelativePath,
            (p.FirstTicks ?? 0) / QpcClock.TicksPerMillisecond,
            p.FirstTicks is null || p.LastTicks is null ? 0 : (p.LastTicks.Value + _pacer.PeriodTicks - p.FirstTicks.Value) / QpcClock.TicksPerMillisecond,
            p.Encoder.Name,
            p.Encoder.IsHardware,
            p.Frames)).ToList();

    /// <summary>Stops the track: drains the encoder, closes the file and returns the manifest entry.</summary>
    public async Task<VideoTrack> StopAsync(CancellationToken cancellationToken)
    {
        _stopped = true;
        _stop.Set();
        _thread?.Join();
        IVideoTrackWriter? writer;
        lock (_gate)
        {
            writer = _writer;
            _writer = null;
            if (_state != VideoSessionState.Ended)
            {
                _state = VideoSessionState.Stopped;
            }
        }

        if (writer is not null)
        {
            await writer.FinalizeAsync(cancellationToken).ConfigureAwait(false);
            await writer.DisposeAsync().ConfigureAwait(false);
        }

        await _source.StopAsync().ConfigureAwait(false);
        Detach(_source);
        lock (_gate)
        {
            return Describe();
        }
    }

    private VideoTrack Describe()
    {
        var parts = Parts();
        var first = parts.Count == 0 ? null : parts[0];
        var settings = _settings;
        return new VideoTrack
        {
            Id = _options.TrackId,
            Role = _options.Role,
            SourceKind = SourceKindName(_source.Id.Kind),
            SourceId = _source.Id.ToString(),
            Name = _source.Name,
            File = first?.File ?? _options.RelativePartPath(1),
            Parts = parts,
            Width = settings.Width,
            Height = settings.Height,
            FrameRate = settings.FrameRate,
            Quality = settings.Quality.ToString().ToLowerInvariant(),
            BitrateBitsPerSecond = settings.BitrateBitsPerSecond,
            Encoder = first?.Encoder ?? string.Empty,
            HardwareEncoder = first?.HardwareEncoder ?? false,
            StartOffsetMs = first?.StartOffsetMs ?? 0,
            DurationMs = DurationMs(),
            FramesWritten = _pacer.FramesWritten,
            FramesDropped = _pacer.FramesDropped,
            EndedEarlyAtMs = _endedAtMs,
            EndReason = _endReason,
        };
    }

    internal static string SourceKindName(VideoSourceKind kind) => kind switch
    {
        VideoSourceKind.Display => "display",
        VideoSourceKind.Window => "window",
        _ => "camera",
    };

    public async ValueTask DisposeAsync()
    {
        if (!_stopped && _state != VideoSessionState.Created)
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
        }

        await _source.DisposeAsync().ConfigureAwait(false);
        _stop.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Video track {Track} part {Part}: {Encoder} (hardware {Hardware}), {Width}x{Height} {Fps} fps")]
    private partial void LogPart(string track, int part, string encoder, bool hardware, int width, int height, int fps);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Video track {Track}: {Code} at {At} ms")]
    private partial void LogProblem(string track, VideoErrorCode code, long at);

    private sealed class PartInfo(string relativePath, H264EncoderInfo encoder)
    {
        public string RelativePath { get; } = relativePath;

        public H264EncoderInfo Encoder { get; } = encoder;

        public long? FirstTicks { get; set; }

        public long? LastTicks { get; set; }

        public long Frames { get; set; }
    }
}
