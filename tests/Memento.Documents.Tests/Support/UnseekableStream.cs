namespace Memento.Documents.Tests.Support;

/// <summary>Wraps a stream so it cannot seek or report a length (like a network or pipe stream), optionally slowly.</summary>
internal sealed class UnseekableStream(Stream inner, TimeSpan delayPerRead = default) : Stream
{
    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, Math.Min(count, 1024));

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (delayPerRead > TimeSpan.Zero)
        {
            await Task.Delay(delayPerRead, cancellationToken);
        }

        return await inner.ReadAsync(buffer[..Math.Min(buffer.Length, 1024)], cancellationToken);
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
