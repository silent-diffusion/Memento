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

        File.Move(temporary, path, overwrite: true);
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
