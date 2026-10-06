namespace Memento.Core.Audio;

/// <summary>
/// Streams interleaved PCM to a <c>.wav</c> file. The header is written up front with zero sizes;
/// <see cref="Checkpoint"/> flushes everything to disk and patches the RIFF and data sizes, so after a
/// checkpoint the file on disk is a valid WAV up to that point even if the process dies next.
/// Memory use is one fixed buffer. Not thread-safe: one writer thread per track owns an instance.
/// </summary>
public sealed class StreamingWavWriter : IDisposable
{
    private const int BufferSize = 64 * 1024;

    private readonly FileStream _stream;
    private readonly byte[] _buffer = new byte[BufferSize];
    private int _buffered;
    private long _dataBytes;
    private bool _disposed;

    /// <summary>Creates <paramref name="path"/> (failing if it exists) and writes the header.</summary>
    public StreamingWavWriter(string path, PcmFormat format)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(format);
        format.Validate();
        Path = path;
        Format = format;

        // bufferSize 0: the writer owns the only buffer, so Abandon can drop it the way a crash would.
        _stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, bufferSize: 0, FileOptions.None);
        try
        {
            _stream.Write(WavLayout.BuildHeader(format, 0));
            _stream.Flush(flushToDisk: true);
        }
        catch
        {
            _stream.Dispose();
            throw;
        }
    }

    public string Path { get; }

    public PcmFormat Format { get; }

    /// <summary>Bytes of sample data accepted so far (not all of them are necessarily on disk yet).</summary>
    public long DataBytes => _dataBytes;

    /// <summary>Data bytes covered by the header on disk as of the last checkpoint.</summary>
    public long CheckpointedBytes { get; private set; }

    public long DurationMs => Format.BytesToMilliseconds(_dataBytes);

    /// <summary>Appends sample bytes. Partial frames are allowed across calls but should not end a file.</summary>
    /// <exception cref="IOException">The write failed; <see cref="DiskErrors.IsDiskFull"/> tells a full drive apart.</exception>
    public void Write(ReadOnlySpan<byte> samples)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        while (!samples.IsEmpty)
        {
            var take = Math.Min(samples.Length, BufferSize - _buffered);
            samples[..take].CopyTo(_buffer.AsSpan(_buffered));
            _buffered += take;
            _dataBytes += take;
            samples = samples[take..];
            if (_buffered == BufferSize)
            {
                FlushBuffer();
            }
        }
    }

    /// <summary>Flushes all buffered samples to disk and rewrites the header sizes to match.</summary>
    /// <exception cref="IOException">The flush failed; samples already on disk stay valid up to the previous checkpoint.</exception>
    public void Checkpoint()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        FlushBuffer();
        _stream.Flush(flushToDisk: true);
        var end = _stream.Position;
        _stream.Seek(0, SeekOrigin.Begin);
        _stream.Write(WavLayout.BuildHeader(Format, _dataBytes));
        _stream.Seek(end, SeekOrigin.Begin);
        _stream.Flush(flushToDisk: true);
        CheckpointedBytes = _dataBytes;
    }

    /// <summary>
    /// Closes the file without patching the header, as if the process had been killed.
    /// <paramref name="flushBufferedSamples"/> chooses whether samples still in the buffer reach the file
    /// (the OS had them) or are lost (they were still in process memory). For tests and simulations.
    /// </summary>
    internal void Abandon(bool flushBufferedSamples)
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            if (flushBufferedSamples)
            {
                FlushBuffer();
            }
        }
        finally
        {
            _stream.Dispose();
        }
    }

    /// <summary>Checkpoints and closes the file. If that fails the file is left for <see cref="WavRepair"/>.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            Checkpoint();
        }
        finally
        {
            _disposed = true;
            _stream.Dispose();
        }
    }

    private void FlushBuffer()
    {
        if (_buffered == 0)
        {
            return;
        }

        var count = _buffered;
        _buffered = 0;
        try
        {
            _stream.Write(_buffer, 0, count);
        }
        catch (IOException)
        {
            // Some or none of the buffer reached the file. Count only whole frames that did, so the
            // header patched on close describes exactly the samples that are really there.
            ResyncWithFile();
            throw;
        }
    }

    private void ResyncWithFile()
    {
        try
        {
            var onDisk = Math.Max(0, _stream.Length - WavLayout.HeaderSize);
            var whole = onDisk - (onDisk % Format.BlockAlign);
            if (whole != onDisk)
            {
                _stream.SetLength(WavLayout.HeaderSize + whole);
            }

            _stream.Seek(WavLayout.HeaderSize + whole, SeekOrigin.Begin);
            _dataBytes = whole;
        }
        catch (IOException)
        {
            // The handle itself is unusable; the next checkpoint will fail and WavRepair fixes the file later.
        }
    }
}
