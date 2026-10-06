using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Memento.Core.Projects;

/// <summary>Writes JSON to <c>&lt;name&gt;.tmp</c>, flushes it to disk, then moves it over the real file.</summary>
internal static class AtomicJsonFile
{
    public static async Task WriteAsync<T>(string path, T value, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken)
    {
        var temporary = path + ".tmp";
        await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 16 * 1024, useAsync: true))
        {
            await JsonSerializer.SerializeAsync(stream, value, typeInfo, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            stream.Flush(flushToDisk: true);
        }

        // Another reader (a virus scanner, the indexer) can hold the target for a moment; try again briefly.
        await RetryAsync(() => { File.Move(temporary, path, overwrite: true); return Task.CompletedTask; }, cancellationToken);
    }

    /// <summary>Reads a whole text file while letting writers append to it or replace it.</summary>
    public static async Task<string> ReadSharedTextAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 16 * 1024, useAsync: true);
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    /// <summary>Runs <paramref name="action"/>, retrying sharing violations up to five times over about half a second.</summary>
    public static async Task RetryAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await action();
                return;
            }
            catch (Exception ex) when (attempt < 5 && ex is IOException or UnauthorizedAccessException && !Audio.DiskErrors.IsDiskFull(ex))
            {
                await Task.Delay(25 * attempt, cancellationToken);
            }
        }
    }

    /// <summary>Reads and deserializes <paramref name="path"/>; <c>null</c> when it does not exist.</summary>
    /// <exception cref="JsonException">The file is not valid JSON for <typeparamref name="T"/>.</exception>
    public static async Task<T?> ReadAsync<T>(string path, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken)
        where T : class
    {
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 16 * 1024, useAsync: true);
        return await JsonSerializer.DeserializeAsync(stream, typeInfo, cancellationToken);
    }
}
