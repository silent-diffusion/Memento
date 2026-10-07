namespace Memento.Documents.Agenda.OpenXml;

/// <summary>
/// A read-only view of a stream that throws <see cref="InvalidDataException"/> (and sets <see cref="Exceeded"/>) once more than a set number of bytes
/// has come out of it, so a ZIP entry that inflates past what its header declared (or past the package's budget) is
/// stopped while it is being read rather than after it has filled memory.
/// </summary>
internal sealed class BoundedReadStream(Stream inner, long limit) : Stream
{
    public long BytesRead { get; private set; }

    /// <summary>Whether reading stopped because the limit was passed.</summary>
    public bool Exceeded { get; private set; }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => BytesRead;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var read = inner.Read(buffer);
        BytesRead += read;
        if (BytesRead > limit)
        {
            Exceeded = true;
            throw new InvalidDataException("The ZIP entry inflates past its limit.");
        }

        return read;
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
        }

        base.Dispose(disposing);
    }
}
