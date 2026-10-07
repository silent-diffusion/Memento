namespace Memento.Documents.Model.Storage;

/// <summary>Atomic writes (CLAUDE.md): write <c>&lt;name&gt;.tmp</c>, flush it to disk, then move it over the real file.</summary>
public static class AtomicFile
{
    public static async Task WriteAllBytesAsync(string path, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporary = path + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 16 * 1024, useAsync: true))
            {
                await stream.WriteAsync(content, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            await MoveWithRetryAsync(temporary, path, cancellationToken);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
    }

    /// <summary>Moves over the target, retrying briefly while a scanner or indexer holds it.</summary>
    private static async Task MoveWithRetryAsync(string from, string to, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(from, to, overwrite: true);
                return;
            }
            catch (Exception ex) when (attempt < 5 && ex is IOException or UnauthorizedAccessException)
            {
                await Task.Delay(25 * attempt, cancellationToken);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
