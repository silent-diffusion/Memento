namespace Memento.AI.Server;

/// <summary>A read-only stream that returns bytes already read from <paramref name="rest"/> before the rest of it, so a reader can look at the start of an answer first.</summary>
internal sealed class PrefixedStream(byte[] prefix, Stream rest) : Stream
{
    private int _position;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (_position < prefix.Length)
        {
            var n = Math.Min(buffer.Length, prefix.Length - _position);
            prefix.AsSpan(_position, n).CopyTo(buffer);
            _position += n;
            return n;
        }

        return rest.Read(buffer);
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_position < prefix.Length)
        {
            var n = Math.Min(buffer.Length, prefix.Length - _position);
            prefix.AsMemory(_position, n).CopyTo(buffer);
            _position += n;
            return n;
        }

        return await rest.ReadAsync(buffer, cancellationToken);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
