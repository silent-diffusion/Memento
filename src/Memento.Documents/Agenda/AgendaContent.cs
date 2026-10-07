namespace Memento.Documents.Agenda;

/// <summary>Reads a source stream into memory once, refusing anything over the size limit before it is parsed.</summary>
internal static class AgendaContent
{
    public static async Task<ReadOnlyMemory<byte>> ReadAsync(Stream content, AgendaParseOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        cancellationToken.ThrowIfCancellationRequested();

        if (content.CanSeek)
        {
            var remaining = content.Length - content.Position;
            if (remaining > options.MaxFileBytes)
            {
                throw AgendaErrors.FileTooLarge(options, remaining);
            }

            if (content is MemoryStream memory && memory.TryGetBuffer(out var segment))
            {
                var start = (int)memory.Position;
                return segment.AsMemory(start, (int)(memory.Length - start));
            }
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        long total = 0;
        while (true)
        {
            var read = await content.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > options.MaxFileBytes)
            {
                throw AgendaErrors.FileTooLarge(options, null);
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}
