using System.Buffers.Binary;

namespace Memento.Audio.Writing;

/// <summary>
/// Append-only RIFF/WAVE writer for live capture (ENGINE-NOTES.md §B).
/// <list type="bullet">
/// <item>The header is written first with zero sizes; audio is appended after it.</item>
/// <item><see cref="Flush"/> hands the managed buffer (64 KiB by default) to the OS; call it about once per second so a process crash loses under a second.</item>
/// <item><see cref="Checkpoint"/> = flush to disk, patch the RIFF and data sizes, flush to disk again. The header never claims bytes that are not on disk.</item>
/// <item><see cref="Repair"/> rebuilds the sizes from the file length after a crash, dropping a trailing partial frame.</item>
/// </list>
/// The writer is not thread-safe; callers serialise access.
/// </summary>
public sealed class StreamingWavWriter : IDisposable
{
    public const int DefaultBufferSize = 64 * 1024;

    /// <summary>Largest data chunk a classic RIFF file can describe.</summary>
    public const long MaxDataBytes = uint.MaxValue - 64;

    private readonly FileStream _file;
    private readonly byte[] _buffer;
    private readonly int _dataSizeOffset;
    private readonly int _dataStart;
    private readonly bool _durable;
    private int _buffered;
    private long _dataBytes;
    private bool _disposed;

    /// <summary>Creates <paramref name="path"/> (it must not exist) and writes the header.</summary>
    public StreamingWavWriter(string path, AudioFormat format, int bufferSize = DefaultBufferSize, bool durableCheckpoints = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(format);
        ArgumentOutOfRangeException.ThrowIfLessThan(bufferSize, format.BlockAlign);

        Path = path;
        Format = format;
        _durable = durableCheckpoints;
        _buffer = new byte[bufferSize];

        // Unbuffered FileStream: this class owns the only managed buffer, so what reached the OS is exactly
        // what Flush() handed over. FileShare.Read lets the UI or a probe read the growing file.
        _file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read, bufferSize: 0);
        var header = WavHeader.Build(format, out _dataSizeOffset);
        _dataStart = header.Length;
        _file.Write(header);
        _file.Flush(_durable);
    }

    public string Path { get; }

    public AudioFormat Format { get; }

    /// <summary>Audio bytes accepted so far (buffered or on disk).</summary>
    public long DataBytes => _dataBytes;

    public long Frames => _dataBytes / Format.BlockAlign;

    public TimeSpan Duration => Format.DurationOf(Frames);

    /// <summary>Total file size once everything buffered is written.</summary>
    public long FileLength => _dataStart + _dataBytes;

    public void Write(ReadOnlySpan<byte> data)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_dataBytes + data.Length > MaxDataBytes)
        {
            throw new InvalidOperationException($"{System.IO.Path.GetFileName(Path)} would exceed the 4 GiB RIFF limit; roll over to a new part first.");
        }

        while (!data.IsEmpty)
        {
            var n = Math.Min(data.Length, _buffer.Length - _buffered);
            data[..n].CopyTo(_buffer.AsSpan(_buffered));
            _buffered += n;
            data = data[n..];
            _dataBytes += n;
            if (_buffered == _buffer.Length)
            {
                DrainBuffer();
            }
        }
    }

    /// <summary>Appends <paramref name="frames"/> frames of digital silence.</summary>
    public void WriteSilence(long frames)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frames);
        var bytes = frames * Format.BlockAlign;
        Span<byte> zeros = stackalloc byte[4096 - (4096 % Format.BlockAlign)];
        zeros.Clear();
        while (bytes > 0)
        {
            var n = (int)Math.Min(bytes, zeros.Length);
            Write(zeros[..n]);
            bytes -= n;
        }
    }

    /// <summary>Hands buffered audio to the operating system (survives a process crash, not a power cut).</summary>
    public void Flush()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        DrainBuffer();
    }

    /// <summary>Makes everything written durable and patches the header to describe it.</summary>
    public void Checkpoint()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        DrainBuffer();
        _file.Flush(_durable);
        var end = _file.Position;
        Span<byte> field = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(field, (uint)(_dataStart - 8 + _dataBytes));
        _file.Position = WavHeader.RiffSizeOffset;
        _file.Write(field);
        BinaryPrimitives.WriteUInt32LittleEndian(field, (uint)_dataBytes);
        _file.Position = _dataSizeOffset;
        _file.Write(field);
        _file.Position = end;
        _file.Flush(_durable);
    }

    /// <summary>Final checkpoint and close.</summary>
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
            _file.Dispose();
        }
    }

    /// <summary>Test hook: closes the file as a crash would, losing the managed buffer and never patching the header.</summary>
    internal void Abandon()
    {
        _disposed = true;
        _buffered = 0;
        _file.Dispose();
    }

    /// <summary>
    /// Rewrites the RIFF and data sizes of <paramref name="path"/> from its length, truncating a trailing
    /// partial frame. Safe to run on a file that is already consistent.
    /// </summary>
    public static WavRepairResult Repair(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var name = System.IO.Path.GetFileName(path);
        using var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var header = WavHeader.Parse(file, name);
        var length = file.Length;
        var available = Math.Max(0, length - header.DataOffset);
        var recovered = Math.Min(available - (available % header.Format.BlockAlign), MaxDataBytes - (MaxDataBytes % header.Format.BlockAlign));
        var truncated = length - (header.DataOffset + recovered);
        if (truncated > 0)
        {
            file.SetLength(header.DataOffset + recovered);
        }

        Span<byte> field = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(field, (uint)(header.DataOffset - 8 + recovered));
        file.Position = WavHeader.RiffSizeOffset;
        file.Write(field);
        BinaryPrimitives.WriteUInt32LittleEndian(field, (uint)recovered);
        file.Position = header.DataSizeFieldOffset;
        file.Write(field);
        file.Flush(true);

        var frames = recovered / header.Format.BlockAlign;
        return new WavRepairResult(path, header.Format, header.DeclaredDataBytes, recovered, truncated, frames, header.Format.DurationOf(frames));
    }

    private void DrainBuffer()
    {
        if (_buffered == 0)
        {
            return;
        }

        var n = _buffered;
        _buffered = 0;
        try
        {
            _file.Write(_buffer, 0, n);
        }
        catch (IOException)
        {
            // Disk full or similar: from now on describe only whole frames that are really in the file,
            // so the final header patch never claims audio that was not written.
            var onDisk = Math.Max(0, _file.Length - _dataStart);
            _dataBytes = onDisk - (onDisk % Format.BlockAlign);
            _file.Position = _dataStart + _dataBytes;
            throw;
        }
    }
}
