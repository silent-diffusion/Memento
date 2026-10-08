namespace Memento.Audio.Writing;

/// <summary>
/// Writes one track: converts capture samples to the storage format (float32 → int24, int16/int24 unchanged),
/// drops frames outside the recording window or inside pauses (by capture time), streams the result into
/// <c>&lt;stem&gt;.wav</c> with rollover, flushes the managed buffer about once per second and checkpoints on demand.
/// Thread-safe: the capture pump writes while the session pauses, resumes and checkpoints.
/// </summary>
public sealed class TrackWriter : IDisposable
{
    private readonly object _sync = new();
    private readonly TrackWriterOptions _options;
    private readonly RollingWavWriter _writer;
    private readonly TimeGate _gate = new();
    private readonly List<FrameRange> _ranges = [];
    private readonly List<PendingGap> _gaps = [];
    private byte[] _scratch = new byte[64 * 1024];
    private long _lastFlush;
    private long? _firstFrameQpc;
    private long _lastFrameEndQpc = long.MinValue;
    private long? _padFrom;
    private bool _disposed;

    public TrackWriter(TrackWriterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        InputFormat = options.InputFormat;
        StorageFormat = PcmConverter.StorageFormatFor(options.InputFormat);
        _writer = new RollingWavWriter(options.Directory, options.FileStem, StorageFormat, options.RolloverBytes, options.BufferSize, options.DurableCheckpoints);
        _lastFlush = options.Clock();
    }

    public AudioFormat InputFormat { get; }

    /// <summary>The format on disk (int24 or int16 PCM).</summary>
    public AudioFormat StorageFormat { get; }

    public string FileStem => _options.FileStem;

    public IReadOnlyList<string> Parts
    {
        get
        {
            lock (_sync)
            {
                return [.. _writer.Parts];
            }
        }
    }

    public long FramesWritten
    {
        get
        {
            lock (_sync)
            {
                return _writer.Frames;
            }
        }
    }

    public TimeSpan Duration => StorageFormat.DurationOf(FramesWritten);

    /// <summary>A jump in capture time longer than this between two packets is written as silence (100 ms).</summary>
    public static readonly long MaxContinuityGapTicks = 100 * QpcClock.TicksPerMillisecond;

    /// <summary>Frames of silence written because the device delivered nothing for a while (sleep, a frozen app).</summary>
    public long FilledGapFrames { get; private set; }

    /// <summary>Capture time of the first frame written, or null before any.</summary>
    public long? FirstFrameQpc
    {
        get
        {
            lock (_sync)
            {
                return _firstFrameQpc;
            }
        }
    }

    /// <summary>Capture time just past the last frame offered (written or dropped), or <see cref="long.MinValue"/>.</summary>
    public long LastFrameEndQpc
    {
        get
        {
            lock (_sync)
            {
                return _lastFrameEndQpc;
            }
        }
    }

    public bool IsPaused
    {
        get
        {
            lock (_sync)
            {
                return _gate.IsPaused;
            }
        }
    }

    /// <summary>Completed pauses inside the track (a pause still open at the end is not a gap).</summary>
    public IReadOnlyList<TrackGap> Gaps
    {
        get
        {
            lock (_sync)
            {
                return _gaps.Where(g => g.ResumedAt != long.MaxValue && g.AtFrame >= 0)
                    .Select(g => new TrackGap(g.AtFrame, StorageFormat.DurationOf(g.AtFrame), TimeSpan.FromTicks(g.ResumedAt - g.PausedAt), g.PausedAt, g.ResumedAt))
                    .ToList();
            }
        }
    }

    /// <summary>Frames captured before <paramref name="qpc"/> are not written (the session start).</summary>
    public void SetStart(long qpc)
    {
        lock (_sync)
        {
            _gate.SetStart(qpc);
        }
    }

    /// <summary>Frames captured at or after <paramref name="qpc"/> are not written (stop, source lost, source disabled).</summary>
    public void End(long qpc)
    {
        lock (_sync)
        {
            _gate.SetEnd(qpc);
        }
    }

    /// <summary>
    /// Before the first frame, write silence back to <paramref name="qpc"/> (pauses still excluded), so the track
    /// starts exactly at the session start instead of after the device's start-up latency.
    /// </summary>
    public void PadStartFrom(long qpc)
    {
        lock (_sync)
        {
            if (_firstFrameQpc is null)
            {
                _padFrom = qpc;
            }
        }
    }

    public void Pause(long qpc)
    {
        lock (_sync)
        {
            if (_gate.IsPaused)
            {
                return;
            }

            _gate.Pause(qpc);
            _gaps.Add(new PendingGap(qpc));
        }
    }

    /// <summary>Ends the pause; returns how long it lasted.</summary>
    public TimeSpan Resume(long qpc)
    {
        lock (_sync)
        {
            var ticks = _gate.Resume(qpc);
            if (ticks > 0 || _gaps.Count > 0)
            {
                var open = _gaps.FindLastIndex(g => g.ResumedAt == long.MaxValue);
                if (open >= 0)
                {
                    _gaps[open].ResumedAt = _gaps[open].PausedAt + ticks;
                }
            }

            return TimeSpan.FromTicks(ticks);
        }
    }

    /// <summary>
    /// Offers one packet in <see cref="InputFormat"/> whose first frame was captured at <paramref name="qpcPosition"/>.
    /// </summary>
    public void Write(ReadOnlySpan<byte> data, long qpcPosition)
    {
        if (data.Length % InputFormat.BlockAlign != 0)
        {
            throw new ArgumentException("Packets must hold whole frames.", nameof(data));
        }

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var frames = data.Length / InputFormat.BlockAlign;
            Offer(qpcPosition, frames, data);
        }
    }

    /// <summary>Offers <paramref name="frames"/> frames of silence starting at <paramref name="qpcPosition"/> (gap fill, overrun cover).</summary>
    public void WriteSilence(int frames, long qpcPosition)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frames);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Offer(qpcPosition, frames, []);
        }
    }

    /// <summary>Hands the buffer to the OS if the flush interval has passed. Called by the pump after each packet.</summary>
    public void FlushIfDue()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            var now = _options.Clock();
            if (now - _lastFlush >= _options.FlushInterval.Ticks)
            {
                _writer.Flush();
                _lastFlush = now;
            }
        }
    }

    public TrackCheckpoint Checkpoint()
    {
        lock (_sync)
        {
            if (!_disposed)
            {
                _writer.Checkpoint();
                _lastFlush = _options.Clock();
            }

            return new TrackCheckpoint([.. _writer.Parts], _writer.DataBytes, _writer.Frames, _writer.Duration);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (var gap in _gaps)
            {
                if (gap.AtFrame < 0)
                {
                    gap.AtFrame = _writer.Frames;
                }
            }

            _writer.Dispose();
        }
    }

    /// <summary>Test hook: simulates a crash (buffer lost, header not patched).</summary>
    internal void Abandon()
    {
        lock (_sync)
        {
            _disposed = true;
            _writer.Abandon();
        }
    }

    private void Offer(long qpc, int frames, ReadOnlySpan<byte> data)
    {
        if (frames == 0)
        {
            return;
        }

        var rate = InputFormat.SampleRate;
        if (_padFrom is { } pad && _firstFrameQpc is null)
        {
            _padFrom = null;
            var padFrames = QpcClock.TicksToFramesRounded(qpc - pad, rate);
            for (long done = 0; done < padFrames;)
            {
                var n = (int)Math.Min(rate, padFrames - done);
                Offer(pad + QpcClock.FramesToTicks(done, rate), n, []);
                done += n;
            }
        }

        // A packet that starts well after the previous one ended means the device delivered nothing in between: the
        // PC slept (Modern Standby suspends desktop apps), Memento was frozen, or the driver dropped its buffer. That
        // time is written as silence, so this track stays in step with the others instead of sliding earlier.
        var gapTicks = _lastFrameEndQpc == long.MinValue ? 0 : qpc - _lastFrameEndQpc;
        if (gapTicks > MaxContinuityGapTicks)
        {
            var gapStart = _lastFrameEndQpc;
            var gapFrames = QpcClock.TicksToFramesRounded(gapTicks, rate);
            var before = _writer.Frames;
            for (long done = 0; done < gapFrames;)
            {
                var n = (int)Math.Min(rate, gapFrames - done);
                Offer(gapStart + QpcClock.FramesToTicks(done, rate), n, []);
                done += n;
            }

            // Only what was written counts: time inside a pause or outside the recording stays out.
            FilledGapFrames += _writer.Frames - before;
        }

        _lastFrameEndQpc = Math.Max(_lastFrameEndQpc, qpc + QpcClock.FramesToTicks(frames, rate));
        _gate.Split(qpc, frames, rate, _ranges);
        foreach (var range in _ranges)
        {
            var rangeStart = qpc + QpcClock.FramesToTicks(range.Offset, rate);
            _firstFrameQpc ??= rangeStart;
            foreach (var gap in _gaps)
            {
                if (gap.AtFrame < 0 && gap.PausedAt <= rangeStart)
                {
                    gap.AtFrame = _writer.Frames;
                }
            }

            if (data.IsEmpty)
            {
                _writer.WriteSilence(range.Count);
                continue;
            }

            var block = InputFormat.BlockAlign;
            var source = data.Slice(range.Offset * block, range.Count * block);
            var needed = range.Count * StorageFormat.BlockAlign;
            if (_scratch.Length < needed)
            {
                _scratch = new byte[needed];
            }

            var n = PcmConverter.ToStorage(source, InputFormat, _scratch);
            _writer.Write(_scratch.AsSpan(0, n));
        }
    }

    private sealed class PendingGap(long pausedAt)
    {
        public long PausedAt { get; } = pausedAt;

        public long ResumedAt { get; set; } = long.MaxValue;

        public long AtFrame { get; set; } = -1;
    }
}
