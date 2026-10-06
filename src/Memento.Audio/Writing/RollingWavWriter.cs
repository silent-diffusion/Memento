namespace Memento.Audio.Writing;

/// <summary>
/// A <see cref="StreamingWavWriter"/> that rolls over to <c>&lt;stem&gt;.part2.wav</c>, <c>.part3.wav</c>, … when a
/// part reaches <see cref="RolloverBytes"/> (3.5 GiB by default, below the 4 GiB RIFF limit). Splits happen on
/// frame boundaries. Not thread-safe.
/// </summary>
public sealed class RollingWavWriter : IDisposable
{
    /// <summary>3.5 GiB: 3 h 40 min of int24 stereo 48 kHz per part.</summary>
    public const long DefaultRolloverBytes = 3584L * 1024 * 1024;

    private readonly List<string> _parts = [];
    private readonly int _bufferSize;
    private readonly bool _durable;
    private StreamingWavWriter _current;
    private long _completedBytes;
    private bool _disposed;

    public RollingWavWriter(
        string directory,
        string stem,
        AudioFormat format,
        long rolloverBytes = DefaultRolloverBytes,
        int bufferSize = StreamingWavWriter.DefaultBufferSize,
        bool durableCheckpoints = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(stem);
        ArgumentNullException.ThrowIfNull(format);
        ArgumentOutOfRangeException.ThrowIfLessThan(rolloverBytes, format.BlockAlign);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(rolloverBytes, StreamingWavWriter.MaxDataBytes);

        Directory = directory;
        Stem = stem;
        Format = format;
        RolloverBytes = rolloverBytes - (rolloverBytes % format.BlockAlign);
        _bufferSize = bufferSize;
        _durable = durableCheckpoints;
        System.IO.Directory.CreateDirectory(directory);
        _current = OpenPart(1);
    }

    public string Directory { get; }

    public string Stem { get; }

    public AudioFormat Format { get; }

    public long RolloverBytes { get; }

    /// <summary>Full paths of every part opened so far, in order.</summary>
    public IReadOnlyList<string> Parts => _parts;

    public long DataBytes => _completedBytes + _current.DataBytes;

    public long Frames => DataBytes / Format.BlockAlign;

    public TimeSpan Duration => Format.DurationOf(Frames);

    /// <summary>Raised after a part is closed and the next one opened (argument: the closed part's path).</summary>
    public event EventHandler<string>? RolledOver;

    /// <summary>Appends whole frames.</summary>
    public void Write(ReadOnlySpan<byte> frames)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (frames.Length % Format.BlockAlign != 0)
        {
            throw new ArgumentException("Writes must hold whole frames.", nameof(frames));
        }

        while (!frames.IsEmpty)
        {
            var room = RolloverBytes - _current.DataBytes;
            if (room == 0)
            {
                Roll();
                continue;
            }

            var n = (int)Math.Min(frames.Length, room);
            _current.Write(frames[..n]);
            frames = frames[n..];
        }
    }

    public void WriteSilence(long frames)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(frames);
        while (frames > 0)
        {
            var roomFrames = (RolloverBytes - _current.DataBytes) / Format.BlockAlign;
            if (roomFrames == 0)
            {
                Roll();
                continue;
            }

            var n = Math.Min(frames, roomFrames);
            _current.WriteSilence(n);
            frames -= n;
        }
    }

    public void Flush()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _current.Flush();
    }

    public void Checkpoint()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _current.Checkpoint();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _current.Dispose();
    }

    /// <summary>Test hook: loses the buffer of the open part as a crash would.</summary>
    internal void Abandon()
    {
        _disposed = true;
        _current.Abandon();
    }

    private void Roll()
    {
        var closed = _current.Path;
        _completedBytes += _current.DataBytes;
        _current.Dispose();
        _current = OpenPart(_parts.Count + 1);
        RolledOver?.Invoke(this, closed);
    }

    private StreamingWavWriter OpenPart(int index)
    {
        var path = System.IO.Path.Combine(Directory, WavTrackSet.PartFileName(Stem, index));
        var writer = new StreamingWavWriter(path, Format, _bufferSize, _durable);
        _parts.Add(path);
        return writer;
    }
}
