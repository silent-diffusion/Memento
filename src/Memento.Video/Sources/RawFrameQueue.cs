namespace Memento.Video.Sources;

/// <summary>
/// The bounded hand-over between a DirectShow camera's streaming thread and Memento's conversion thread
/// (ENGINE-NOTES.md §R). <see cref="TryWrite"/> copies the picture into one of a few reusable buffers and returns at
/// once; when every buffer is taken (the conversion fell behind) the new picture is dropped and counted, so the camera
/// never waits for Memento. The conversion thread takes pictures in order and gives each buffer back.
/// </summary>
internal sealed class RawFrameQueue : IDisposable
{
    private readonly object _gate = new();
    private readonly Queue<RawFrame> _pending = new();
    private readonly Stack<byte[]> _free = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly int _slots;
    private int _allocated;
    private long _written;
    private long _dropped;
    private bool _completed;

    /// <param name="slots">Buffers in all: one being converted and the rest waiting (at least 2).</param>
    public RawFrameQueue(int slots = 3)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(slots, 2);
        _slots = slots;
    }

    /// <summary>Pictures accepted so far.</summary>
    public long Written => Interlocked.Read(ref _written);

    /// <summary>Pictures dropped because every buffer was taken.</summary>
    public long Dropped => Interlocked.Read(ref _dropped);

    /// <summary>Copies a picture in, stamped <paramref name="qpc"/>. Never waits; false when it was dropped.</summary>
    public bool TryWrite(ReadOnlySpan<byte> data, long qpc)
    {
        byte[]? buffer;
        lock (_gate)
        {
            if (_completed)
            {
                return false;
            }

            if (_free.Count > 0)
            {
                buffer = _free.Pop();
            }
            else if (_allocated < _slots)
            {
                _allocated++;
                buffer = null;
            }
            else
            {
                _dropped++;
                return false;
            }
        }

        if (buffer is null || buffer.Length < data.Length)
        {
            // The first pictures (or a larger format): allocate outside the lock.
            buffer = new byte[data.Length];
        }

        data.CopyTo(buffer);
        lock (_gate)
        {
            if (_completed)
            {
                _free.Push(buffer);
                return false;
            }

            _pending.Enqueue(new RawFrame(buffer, data.Length, qpc));
            _written++;
        }

        _signal.Release();
        return true;
    }

    /// <summary>The oldest waiting picture, waiting up to <paramref name="timeout"/>; null on timeout or once completed.</summary>
    public RawFrame? Take(TimeSpan timeout)
    {
        if (!_signal.Wait(timeout))
        {
            return null;
        }

        lock (_gate)
        {
            return _pending.Count > 0 ? _pending.Dequeue() : null;
        }
    }

    /// <summary>Gives a taken picture's buffer back for the next one.</summary>
    public void Return(RawFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        lock (_gate)
        {
            _free.Push(frame.Buffer);
        }
    }

    /// <summary>No more pictures are accepted; a waiting <see cref="Take"/> returns.</summary>
    public void Complete()
    {
        lock (_gate)
        {
            _completed = true;
            _pending.Clear();
        }

        _signal.Release();
    }

    public void Dispose()
    {
        Complete();
        _signal.Dispose();
    }
}
