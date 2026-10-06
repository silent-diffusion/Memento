using System.Buffers;
using System.Diagnostics;
using System.Threading.Channels;
using Memento.Core.Audio;
using Memento.Core.Bridge.Contracts;
using Microsoft.Extensions.Logging;

namespace Memento.Core.Recording.Simulation;

/// <summary>
/// A simulated capture session, structured like the real one: a capture thread produces 10 ms packets for every
/// open track into a bounded per-track ring buffer (it never waits: a full buffer drops the packet and counts an
/// overrun), one writer per track drains its buffer into a <see cref="StreamingWavWriter"/>, and every event goes
/// through a <see cref="SessionEventDispatcher"/>. With <see cref="SimulatedEngineOptions.Speed"/> 0 there is no
/// capture thread and <see cref="Advance"/> produces audio on the caller's thread.
/// </summary>
public sealed partial class SimulatedRecordingSession : IRecordingSession
{
    private const int LevelIntervalMs = 34;

    private readonly object _gate = new();
    private readonly List<TrackSlot> _slots = [];
    private readonly HashSet<string> _reservedIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<SessionPause> _pauses = [];
    private readonly List<Task> _pendingCheckpoints = [];
    private readonly RecordingPlan _plan;
    private readonly SimulatedAudioSourceProvider _sources;
    private readonly SimulatedEngineOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<SimulatedRecordingSession> _logger;
    private readonly SessionEventDispatcher _dispatcher;
    private readonly Stopwatch _wall = new();
    private readonly long _checkpointMs;

    private Thread? _thread;
    private volatile bool _running;
    private volatile bool _diskFullInjected;
    private long _producedClockMs;
    private long _elapsedMs;
    private long _lastCheckpointClockMs;
    private long _pauseStartedClockMs;
    private long _lastLevelsWallMs = -LevelIntervalMs;
    private long _overruns;
    private int _variant;
    private bool _lossTriggered;
    private bool _diskFullTriggered;
    private RecordingSessionState _state = RecordingSessionState.Recording;
    private DateTimeOffset? _lastCheckpointAt;
    private Task<RecordingSessionResult>? _stopTask;

    internal SimulatedRecordingSession(
        string sessionId,
        RecordingPlan plan,
        SimulatedAudioSourceProvider sources,
        SimulatedEngineOptions options,
        TimeProvider time,
        ILogger<SimulatedRecordingSession> logger)
    {
        SessionId = sessionId;
        _plan = plan;
        _sources = sources;
        _options = options;
        _time = time;
        _logger = logger;
        _dispatcher = new SessionEventDispatcher(logger);
        _checkpointMs = Math.Max(1, (long)plan.CheckpointInterval.TotalMilliseconds);
        StartedAt = time.GetLocalNow();
    }

    public event EventHandler<LevelsEventArgs>? LevelsAvailable;

    public event EventHandler<SessionStateChangedEventArgs>? StateChanged;

    public event EventHandler<CheckpointEventArgs>? CheckpointWritten;

    public event EventHandler<SourceLostEventArgs>? SourceLost;

    public event EventHandler<HostStoppedEventArgs>? HostStopped;

    public string SessionId { get; }

    public DateTimeOffset StartedAt { get; }

    public RecordingSessionState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public long ElapsedMs => Interlocked.Read(ref _elapsedMs);

    /// <summary>Packets dropped because a writer fell more than the ring buffer behind.</summary>
    public long Overruns => Interlocked.Read(ref _overruns);

    public IReadOnlyList<SessionTrack> Tracks
    {
        get
        {
            lock (_gate)
            {
                return _slots.Select(s => s.Snapshot()).ToList();
            }
        }
    }

    public IReadOnlyList<SessionPause> Pauses
    {
        get
        {
            lock (_gate)
            {
                return _pauses.ToList();
            }
        }
    }

    public DateTimeOffset? LastCheckpointAt
    {
        get
        {
            lock (_gate)
            {
                return _lastCheckpointAt;
            }
        }
    }

    /// <summary>Manual mode only: produces <paramref name="duration"/> of audio on the calling thread.</summary>
    public void Advance(TimeSpan duration)
    {
        if (_options.Speed > 0)
        {
            throw new InvalidOperationException("Advance is only available when the simulated engine runs with Speed 0.");
        }

        long target;
        lock (_gate)
        {
            target = _producedClockMs + (long)duration.TotalMilliseconds;
        }

        Pump(target);
    }

    /// <summary>Waits for checkpoints already started and for queued events to be delivered.</summary>
    public async Task DrainAsync()
    {
        Task[] pending;
        lock (_gate)
        {
            pending = [.. _pendingCheckpoints];
        }

        await Task.WhenAll(pending);
        await _dispatcher.DrainAsync();
    }

    /// <summary>Unplugs a source: its track ends now, the others continue.</summary>
    public async Task SimulateSourceLostAsync(string sourceId)
    {
        TrackSlot? slot;
        List<TrackSlot> remaining;
        long at;
        lock (_gate)
        {
            slot = _slots.FirstOrDefault(s => s.Source.Id == sourceId && s.IsOpen);
            if (slot is null || _stopTask is not null)
            {
                return;
            }

            at = _elapsedMs;
            slot.End(at, TrackEndReason.SourceLost);
            remaining = _slots.Where(s => s.IsOpen).ToList();
        }

        _sources.SetAvailable(sourceId, false);
        await CloseSlotAsync(slot);
        LogSourceLost(SessionId, slot.TrackId, at);
        var lost = slot.Snapshot();
        var remainingSnapshot = remaining.Select(s => s.Snapshot()).ToList();
        _dispatcher.Post(() => SourceLost?.Invoke(this, new SourceLostEventArgs(lost, at, remainingSnapshot)));
        if (remaining.Count == 0)
        {
            TriggerHostStop(HostStopReason.DeviceLost, at, "Every source was lost.");
        }
    }

    /// <summary>From the next packet on, every write fails as if the drive were full.</summary>
    public void SimulateDiskFull() => _diskFullInjected = true;

    /// <summary>
    /// Simulates the process being killed: capture stops and every writer closes its file without patching the
    /// header. <paramref name="flushBufferedSamples"/> chooses whether the writers' in-memory buffers reach the disk.
    /// No events are raised; <see cref="StopAsync"/> fails afterwards.
    /// </summary>
    public async Task SimulateCrashAsync(bool flushBufferedSamples)
    {
        StopGenerator();
        List<TrackSlot> slots;
        lock (_gate)
        {
            _state = RecordingSessionState.Stopped;
            _stopTask ??= Task.FromException<RecordingSessionResult>(new InvalidOperationException("The simulated session crashed."));
            slots = _slots.Where(s => !s.Closing).ToList();
            foreach (var slot in slots)
            {
                slot.Closing = true;
            }
        }

        foreach (var slot in slots)
        {
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await slot.Queue.Writer.WriteAsync(new AbandonCommand(flushBufferedSamples, done));
            slot.Queue.Writer.TryComplete();
            await done.Task;
        }

        await Task.WhenAll(_slots.Select(s => s.Loop));
    }

    public Task PauseAsync(CancellationToken cancellationToken)
    {
        long elapsed;
        lock (_gate)
        {
            if (_state != RecordingSessionState.Recording || _stopTask is not null)
            {
                return Task.CompletedTask;
            }

            _state = RecordingSessionState.Paused;
            elapsed = _elapsedMs;
            _pauses.Add(new SessionPause(elapsed, _time.GetLocalNow(), null));
            _pauseStartedClockMs = _producedClockMs;
        }

        _dispatcher.Post(() => StateChanged?.Invoke(this, new SessionStateChangedEventArgs(RecordingSessionState.Paused, elapsed)));
        ScheduleCheckpoint();
        return Task.CompletedTask;
    }

    public Task ResumeAsync(CancellationToken cancellationToken)
    {
        long elapsed;
        lock (_gate)
        {
            if (_state != RecordingSessionState.Paused || _stopTask is not null)
            {
                return Task.CompletedTask;
            }

            ClosePauseLocked();
            _state = RecordingSessionState.Recording;
            elapsed = _elapsedMs;
        }

        _dispatcher.Post(() => StateChanged?.Invoke(this, new SessionStateChangedEventArgs(RecordingSessionState.Recording, elapsed)));
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<SessionTrack>> SetSourceAsync(AudioSource source, bool enabled, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (enabled)
        {
            lock (_gate)
            {
                ThrowIfStopping();
                if (_slots.Any(s => s.Source.Id == source.Id && s.IsOpen))
                {
                    return Tracks;
                }
            }

            if (!_sources.IsAvailable(source.Id))
            {
                throw new SourceUnavailableException(source.Id, source.Name, "the device is not connected");
            }

            var slot = OpenSlot(source);
            lock (_gate)
            {
                slot.StartOffsetMs = _elapsedMs;
                _slots.Add(slot);
            }

            slot.Loop = Task.Run(() => RunWriterAsync(slot), CancellationToken.None);
            LogTrackStarted(SessionId, slot.TrackId, slot.StartOffsetMs);
        }
        else
        {
            TrackSlot? slot;
            lock (_gate)
            {
                slot = _slots.FirstOrDefault(s => s.Source.Id == source.Id && s.IsOpen);
                if (slot is null)
                {
                    return Tracks;
                }

                slot.End(_elapsedMs, TrackEndReason.Disabled);
            }

            await CloseSlotAsync(slot);
            LogTrackEnded(SessionId, slot.TrackId, slot.EndedAtMs ?? 0);
        }

        return Tracks;
    }

    public Task<long> MarkAsync(CancellationToken cancellationToken) => Task.FromResult(ElapsedMs);

    public Task CheckpointAsync(CancellationToken cancellationToken) => RunCheckpointAsync();

    public Task<RecordingSessionResult> StopAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _stopTask ??= Task.Run(() => StopCoreAsync(null, 0, null), CancellationToken.None);
            return _stopTask;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await StopAsync(CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
            // Crashed on purpose; the files are left as they are for recovery.
        }

        await _dispatcher.DrainAsync();
        await _dispatcher.DisposeAsync();
    }

    internal void Start()
    {
        Directory.CreateDirectory(Path.Combine(_plan.ProjectFolder, "tracks"));
        var opened = new List<TrackSlot>();
        try
        {
            foreach (var source in _plan.Sources)
            {
                opened.Add(OpenSlot(source));
            }
        }
        catch
        {
            foreach (var slot in opened)
            {
                slot.Writer.Dispose();
            }

            throw;
        }

        lock (_gate)
        {
            _slots.AddRange(opened);
        }

        foreach (var slot in opened)
        {
            slot.Loop = Task.Run(() => RunWriterAsync(slot), CancellationToken.None);
        }

        _wall.Start();
        if (_options.Speed > 0)
        {
            _running = true;
            _thread = new Thread(RunGenerator) { IsBackground = true, Name = "Simulated capture", Priority = ThreadPriority.AboveNormal };
            _thread.Start();
        }

        LogStarted(SessionId, opened.Count, _options.Speed);
    }

    private TrackSlot OpenSlot(AudioSource source)
    {
        string trackId;
        string file;
        int variant;
        lock (_gate)
        {
            (trackId, file) = TrackNaming.Allocate(source, _reservedIds);
            _reservedIds.Add(trackId);
            variant = _variant++;
        }

        var format = _plan.PreferredFormat ?? SimulatedAudioSourceProvider.FormatOf(source);
        if (format.Encoding != SampleEncoding.Pcm || format.BitsPerSample != 16)
        {
            format = PcmFormat.Pcm16(format.SampleRate, format.Channels);
        }

        var path = Path.Combine(_plan.ProjectFolder, file.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var writer = new StreamingWavWriter(path, format);
        var queue = Channel.CreateBounded<WriterCommand>(new BoundedChannelOptions(_options.BufferBlocks)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
        });
        return new TrackSlot(trackId, source, file, format, writer, new SimulatedSignal(source.Kind, format, variant, _options.Seed + variant), queue);
    }

    private void RunGenerator()
    {
        var sleep = Math.Clamp((int)(_options.BlockMs / _options.Speed), 1, 10);
        while (_running)
        {
            Pump((long)(_wall.Elapsed.TotalMilliseconds * _options.Speed));
            Thread.Sleep(sleep);
        }
    }

    /// <summary>Produces packets up to <paramref name="targetClockMs"/> of simulated time. The capture path: never waits.</summary>
    private void Pump(long targetClockMs)
    {
        var block = _options.BlockMs;
        while (true)
        {
            Action? deferred = null;
            lock (_gate)
            {
                if (_stopTask is not null || _producedClockMs + block > targetClockMs)
                {
                    return;
                }

                _producedClockMs += block;
                var recording = _state == RecordingSessionState.Recording;
                foreach (var slot in _slots)
                {
                    if (!slot.IsOpen)
                    {
                        continue;
                    }

                    var frames = slot.Format.SampleRate * block / 1000;
                    var bytes = frames * slot.Format.BlockAlign;
                    var buffer = ArrayPool<byte>.Shared.Rent(bytes);
                    var (rms, peak) = slot.Signal.Fill(buffer.AsSpan(0, bytes), frames);
                    slot.Accumulate(rms, peak);
                    if (!recording || !slot.Queue.Writer.TryWrite(new DataCommand(buffer, bytes)))
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                        if (recording)
                        {
                            _overruns++;
                        }
                    }
                }

                if (recording)
                {
                    _elapsedMs += block;
                }

                if (_producedClockMs - _lastCheckpointClockMs >= _checkpointMs)
                {
                    _lastCheckpointClockMs = _producedClockMs;
                    deferred += ScheduleCheckpoint;
                }

                if (!_lossTriggered && _options.LoseSourceAfter is { } lose && _producedClockMs >= lose.TotalMilliseconds)
                {
                    _lossTriggered = true;
                    var id = _options.LoseSourceId ?? SimulatedAudioSourceProvider.SystemAudio.Id;
                    deferred += () => _ = Task.Run(() => SimulateSourceLostAsync(id));
                }

                if (!_diskFullTriggered && _options.DiskFullAfter is { } full && _producedClockMs >= full.TotalMilliseconds)
                {
                    _diskFullTriggered = true;
                    _diskFullInjected = true;
                }
            }

            EmitLevelsIfDue();
            deferred?.Invoke();
        }
    }

    private void EmitLevelsIfDue()
    {
        var now = _wall.ElapsedMilliseconds;
        if (now - _lastLevelsWallMs < LevelIntervalMs)
        {
            return;
        }

        _lastLevelsWallMs = now;
        List<SourceLevel> levels;
        lock (_gate)
        {
            levels = _slots.Where(s => s.IsOpen && s.HasLevel).Select(s => s.TakeLevel()).ToList();
        }

        if (levels.Count > 0)
        {
            var args = new LevelsEventArgs(levels);
            _dispatcher.PostLevels(() => LevelsAvailable?.Invoke(this, args));
        }
    }

    private void ScheduleCheckpoint()
    {
        lock (_gate)
        {
            if (_stopTask is not null)
            {
                return;
            }

            _pendingCheckpoints.RemoveAll(t => t.IsCompleted);
            _pendingCheckpoints.Add(Task.Run(RunCheckpointAsync));
        }
    }

    private async Task RunCheckpointAsync()
    {
        var waits = new List<(TrackSlot Slot, TaskCompletionSource Done)>();
        long elapsed;
        lock (_gate)
        {
            elapsed = _elapsedMs;
            foreach (var slot in _slots.Where(s => !s.Closing))
            {
                waits.Add((slot, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)));
            }
        }

        foreach (var (slot, done) in waits)
        {
            try
            {
                await slot.Queue.Writer.WriteAsync(new CheckpointCommand(done));
            }
            catch (ChannelClosedException)
            {
                done.TrySetResult();
            }
        }

        await Task.WhenAll(waits.Select(w => w.Done.Task));
        var at = _time.GetLocalNow();
        lock (_gate)
        {
            _lastCheckpointAt = at;
        }

        var tracks = Tracks;
        _dispatcher.Post(() => CheckpointWritten?.Invoke(this, new CheckpointEventArgs(at, elapsed, tracks)));
    }

    private async Task CloseSlotAsync(TrackSlot slot)
    {
        lock (_gate)
        {
            if (slot.Closing)
            {
                return;
            }

            slot.Closing = true;
        }

        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await slot.Queue.Writer.WriteAsync(new CloseCommand(done));
        slot.Queue.Writer.TryComplete();
        await done.Task;
    }

    private void TriggerHostStop(HostStopReason reason, long atMs, string detail)
    {
        lock (_gate)
        {
            _stopTask ??= Task.Run(() => StopCoreAsync(reason, atMs, detail), CancellationToken.None);
        }
    }

    private async Task<RecordingSessionResult> StopCoreAsync(HostStopReason? reason, long atMs, string? detail)
    {
        StopGenerator();
        long elapsed;
        Task[] checkpoints;
        lock (_gate)
        {
            if (_state == RecordingSessionState.Paused)
            {
                ClosePauseLocked();
            }

            _state = RecordingSessionState.Stopped;
            elapsed = _elapsedMs;
            checkpoints = [.. _pendingCheckpoints];
        }

        await Task.WhenAll(checkpoints);
        foreach (var slot in _slots.ToList())
        {
            await CloseSlotAsync(slot);
        }

        await Task.WhenAll(_slots.Select(s => s.Loop));
        var result = new RecordingSessionResult(elapsed, Tracks, Pauses, _time.GetLocalNow(), reason);
        LogStopped(SessionId, elapsed, reason?.ToString() ?? "user", Overruns);
        _dispatcher.Post(() => StateChanged?.Invoke(this, new SessionStateChangedEventArgs(RecordingSessionState.Stopped, elapsed)));
        if (reason is { } stopReason)
        {
            _dispatcher.Post(() => HostStopped?.Invoke(this, new HostStoppedEventArgs(stopReason, atMs, detail ?? string.Empty, result)));
        }

        return result;
    }

    private void StopGenerator()
    {
        _running = false;
        if (_thread is { } thread && thread != Thread.CurrentThread)
        {
            thread.Join();
        }
    }

    private void ClosePauseLocked()
    {
        var open = _pauses[^1];
        _pauses[^1] = open with { DurationMs = _producedClockMs - _pauseStartedClockMs };
    }

    private void ThrowIfStopping()
    {
        if (_stopTask is not null)
        {
            throw new InvalidOperationException("The recording session has stopped.");
        }
    }

    /// <summary>The writer: drains the ring buffer into the WAV writer. Owns the writer exclusively.</summary>
    private async Task RunWriterAsync(TrackSlot slot)
    {
        var closed = false;
        await foreach (var command in slot.Queue.Reader.ReadAllAsync())
        {
            switch (command)
            {
                case DataCommand data:
                    try
                    {
                        if (!closed && !slot.WriteFailed)
                        {
                            if (_diskFullInjected)
                            {
                                throw DiskErrors.CreateDiskFull(slot.Writer.Path);
                            }

                            slot.Writer.Write(data.Buffer.AsSpan(0, data.Length));
                            slot.Written = slot.Writer.DataBytes;
                        }
                    }
                    catch (IOException ex)
                    {
                        slot.WriteFailed = true;
                        slot.Written = slot.Writer.DataBytes;
                        OnWriteFailed(slot, ex);
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(data.Buffer);
                    }

                    break;
                case CheckpointCommand checkpoint:
                    if (!closed && !slot.WriteFailed)
                    {
                        try
                        {
                            slot.Writer.Checkpoint();
                            slot.Checkpointed = slot.Writer.CheckpointedBytes;
                        }
                        catch (IOException ex)
                        {
                            slot.WriteFailed = true;
                            OnWriteFailed(slot, ex);
                        }
                    }

                    checkpoint.Done.TrySetResult();
                    break;
                case CloseCommand close:
                    if (!closed)
                    {
                        closed = true;
                        try
                        {
                            slot.Writer.Dispose();
                        }
                        catch (IOException ex)
                        {
                            LogCloseFailed(ex, slot.TrackId);
                        }

                        slot.Written = slot.Writer.DataBytes;
                        slot.Checkpointed = slot.Writer.CheckpointedBytes;
                    }

                    close.Done.TrySetResult();
                    break;
                case AbandonCommand abandon:
                    if (!closed)
                    {
                        closed = true;
                        slot.Writer.Abandon(abandon.FlushBufferedSamples);
                    }

                    abandon.Done.TrySetResult();
                    break;
            }
        }
    }

    private void OnWriteFailed(TrackSlot slot, IOException exception)
    {
        var at = slot.StartOffsetMs + slot.Format.BytesToMilliseconds(slot.Written);
        if (DiskErrors.IsDiskFull(exception))
        {
            LogDiskFull(SessionId, slot.TrackId, at);
            TriggerHostStop(HostStopReason.DiskFull, at, exception.Message);
        }
        else
        {
            LogWriteFailed(exception, SessionId, slot.TrackId);
            TriggerHostStop(HostStopReason.Error, at, exception.Message);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Simulated session {SessionId} started with {Tracks} tracks at speed {Speed}")]
    private partial void LogStarted(string sessionId, int tracks, double speed);

    [LoggerMessage(Level = LogLevel.Information, Message = "Simulated session {SessionId} stopped at {ElapsedMs} ms ({Reason}); {Overruns} packets dropped")]
    private partial void LogStopped(string sessionId, long elapsedMs, string reason, long overruns);

    [LoggerMessage(Level = LogLevel.Information, Message = "Session {SessionId}: track {TrackId} started at {AtMs} ms")]
    private partial void LogTrackStarted(string sessionId, string trackId, long atMs);

    [LoggerMessage(Level = LogLevel.Information, Message = "Session {SessionId}: track {TrackId} ended at {AtMs} ms")]
    private partial void LogTrackEnded(string sessionId, string trackId, long atMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Session {SessionId}: source of track {TrackId} lost at {AtMs} ms")]
    private partial void LogSourceLost(string sessionId, string trackId, long atMs);

    [LoggerMessage(Level = LogLevel.Error, Message = "Session {SessionId}: the drive is full; track {TrackId} stopped at {AtMs} ms")]
    private partial void LogDiskFull(string sessionId, string trackId, long atMs);

    [LoggerMessage(Level = LogLevel.Error, Message = "Session {SessionId}: writing track {TrackId} failed")]
    private partial void LogWriteFailed(Exception exception, string sessionId, string trackId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Track {TrackId} could not be closed cleanly; recovery will repair its header")]
    private partial void LogCloseFailed(Exception exception, string trackId);

    private abstract record WriterCommand;

    private sealed record DataCommand(byte[] Buffer, int Length) : WriterCommand;

    private sealed record CheckpointCommand(TaskCompletionSource Done) : WriterCommand;

    private sealed record CloseCommand(TaskCompletionSource Done) : WriterCommand;

    private sealed record AbandonCommand(bool FlushBufferedSamples, TaskCompletionSource Done) : WriterCommand;

    private sealed class TrackSlot(
        string trackId,
        AudioSource source,
        string file,
        PcmFormat format,
        StreamingWavWriter writer,
        SimulatedSignal signal,
        Channel<WriterCommand> queue)
    {
        private long _written;
        private long _checkpointed;
        private double _levelSumSquares;
        private float _levelPeak;
        private int _levelCount;

        public string TrackId { get; } = trackId;

        public AudioSource Source { get; } = source;

        public string File { get; } = file;

        public PcmFormat Format { get; } = format;

        public StreamingWavWriter Writer { get; } = writer;

        public SimulatedSignal Signal { get; } = signal;

        public Channel<WriterCommand> Queue { get; } = queue;

        public Task Loop { get; set; } = Task.CompletedTask;

        public long StartOffsetMs { get; set; }

        public long? EndedAtMs { get; private set; }

        public TrackEndReason? EndReason { get; private set; }

        /// <summary>Set under the session lock once a close or abandon has been requested.</summary>
        public bool Closing { get; set; }

        /// <summary>Writer thread only.</summary>
        public bool WriteFailed { get; set; }

        public bool IsOpen => EndReason is null && !Closing;

        public bool HasLevel => _levelCount > 0;

        public long Written
        {
            get => Interlocked.Read(ref _written);
            set => Interlocked.Exchange(ref _written, value);
        }

        public long Checkpointed
        {
            get => Interlocked.Read(ref _checkpointed);
            set => Interlocked.Exchange(ref _checkpointed, value);
        }

        public void End(long atMs, TrackEndReason reason)
        {
            EndedAtMs = atMs;
            EndReason = reason;
        }

        public void Accumulate(float rms, float peak)
        {
            _levelSumSquares += rms * rms;
            _levelPeak = MathF.Max(_levelPeak, peak);
            _levelCount++;
        }

        public SourceLevel TakeLevel()
        {
            var rms = Math.Sqrt(_levelSumSquares / Math.Max(1, _levelCount));
            var level = new SourceLevel(Source.Id, Math.Round(Math.Min(1, rms), 3), Math.Round(Math.Min(1, _levelPeak), 3));
            _levelSumSquares = 0;
            _levelPeak = 0;
            _levelCount = 0;
            return level;
        }

        public SessionTrack Snapshot() =>
            new(TrackId, Source, File, Format, StartOffsetMs, Written, Checkpointed, EndedAtMs, EndReason);
    }
}
