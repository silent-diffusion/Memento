using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Memento.Core.Library;
using Memento.Core.Projects;
using Microsoft.Extensions.Logging;

namespace Memento.Core.Voices;

/// <summary>
/// <c>&lt;library&gt;\voices\known.json</c> (<see cref="KnownVoicesDocument"/>): read through
/// <see cref="KnownVoicesMigration"/>, written atomically (<c>.tmp</c>, then replace), one change at a time. A file
/// that is not JSON is set aside as <c>known.json.damaged-&lt;time&gt;</c> (never deleted) and Memento starts with no
/// known voices; one from a newer Memento is refused and left untouched.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "The write lock never allocates a wait handle; the store lives as long as the app.")]
public sealed partial class KnownVoicesStore(ILibraryLocation library, TimeProvider time, ILogger<KnownVoicesStore> logger)
{
    public const string Folder = "voices";
    public const string FileName = "known.json";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ILogger<KnownVoicesStore> _logger = logger;

    public string FilePath => Path.Combine(library.Root, Folder, FileName);

    /// <exception cref="KnownVoicesNewerException">The file was written by a newer Memento.</exception>
    public async Task<KnownVoicesDocument> LoadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await ReadAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Reads the voices, applies <paramref name="update"/> and writes its document when it returns one (<c>null</c> writes
    /// nothing). Changes are applied one at a time.
    /// </summary>
    public async Task<T> UpdateAsync<T>(Func<KnownVoicesDocument, (KnownVoicesDocument? Next, T Result)> update, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var current = await ReadAsync(cancellationToken);
            var (next, result) = update(current);
            if (next is not null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                await AtomicJsonFile.WriteAsync(FilePath, next with { SchemaVersion = KnownVoicesDocument.CurrentSchemaVersion }, KnownVoicesJsonContext.Default.KnownVoicesDocument, cancellationToken);
            }

            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Forget all: removes the file (and the folder when nothing else is in it). Returns how many voices it held.</summary>
    public async Task<int> DeleteAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var count = 0;
            try
            {
                count = (await ReadAsync(cancellationToken)).Voices.Count;
            }
            catch (KnownVoicesNewerException)
            {
                // Forget all removes a newer file too: the person asked for every voice to go.
            }

            File.Delete(FilePath);
            File.Delete(FilePath + ".tmp");
            var folder = Path.GetDirectoryName(FilePath)!;
            if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
            {
                Directory.Delete(folder);
            }

            LogForgotAll(count);
            return count;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<KnownVoicesDocument> ReadAsync(CancellationToken cancellationToken)
    {
        var path = FilePath;
        if (!File.Exists(path))
        {
            return new KnownVoicesDocument();
        }

        try
        {
            var text = await AtomicJsonFile.ReadSharedTextAsync(path, cancellationToken);
            var document = KnownVoicesMigration.Read(JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }), out var dropped);
            if (dropped > 0)
            {
                LogDropped(dropped);
            }

            return document;
        }
        catch (JsonException ex)
        {
            var aside = path + ".damaged-" + time.GetUtcNow().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
            File.Move(path, aside, overwrite: true);
            LogSetAside(ex, Path.GetFileName(aside));
            return new KnownVoicesDocument();
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "voices/known.json was not readable and was set aside as {File}; Memento starts with no known voices")]
    private partial void LogSetAside(Exception exception, string file);

    [LoggerMessage(Level = LogLevel.Warning, Message = "voices/known.json: {Count} damaged entries were dropped")]
    private partial void LogDropped(int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Forgot every known voice ({Count})")]
    private partial void LogForgotAll(int count);
}
