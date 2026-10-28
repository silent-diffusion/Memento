using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Projects;
using Memento.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Memento.Generation.Ask;

/// <summary>
/// Keeps each recording's Ask conversation in <c>ask/conversation.json</c> in its project folder (2.9.0). Writes are
/// atomic (<c>.tmp</c>, then replace) and serialized per recording. A missing file is an empty conversation. A damaged
/// file is kept beside it as <c>conversation.damaged-{time}.json</c> and the conversation starts again empty. A file from
/// a newer Memento is read for display where it can be, but never written over (<c>ask.newerVersion</c>). The file goes
/// with the project: deleting the recording deletes it.
/// </summary>
public sealed partial class AskConversationStore(IProjectStore projects, TimeProvider time, ILogger<AskConversationStore> logger)
{
    public const string FolderName = "ask";
    public const string FileName = "conversation.json";

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<AskConversationStore> _logger = logger;

    public string PathOf(string recordingId) => Path.Combine(projects.GetProjectFolder(recordingId), FolderName, FileName);

    /// <summary>The conversation's turns and whether the file may be written by this version.</summary>
    public async Task<(IReadOnlyList<AskTurn> Turns, bool Writable)> ReadAsync(string recordingId, CancellationToken cancellationToken)
    {
        var gate = Gate(recordingId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await ReadUnlockedAsync(recordingId, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Adds <paramref name="turn"/> at the end.</summary>
    /// <exception cref="Core.Bridge.BridgeException"><c>ask.newerVersion</c>.</exception>
    public Task<IReadOnlyList<AskTurn>> AppendAsync(string recordingId, AskTurn turn, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(turn);
        return UpdateAsync(recordingId, turns => [.. turns, turn], cancellationToken);
    }

    /// <summary>Removes the file (the designed Clear); an absent file is already clear.</summary>
    /// <exception cref="Core.Bridge.BridgeException"><c>ask.newerVersion</c>.</exception>
    public async Task ClearAsync(string recordingId, CancellationToken cancellationToken)
    {
        var gate = Gate(recordingId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var (_, writable) = await ReadUnlockedAsync(recordingId, cancellationToken);
            if (!writable)
            {
                throw AskErrors.NewerVersion();
            }

            var path = PathOf(recordingId);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>A new turn id: one past the highest <c>tN</c> in <paramref name="turns"/>.</summary>
    public static string NextId(IReadOnlyList<AskTurn> turns)
    {
        ArgumentNullException.ThrowIfNull(turns);
        var highest = turns.Select(t => t.Id.StartsWith('t') && int.TryParse(t.Id.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0).DefaultIfEmpty(0).Max();
        return "t" + (highest + 1).ToString(CultureInfo.InvariantCulture);
    }

    private async Task<IReadOnlyList<AskTurn>> UpdateAsync(string recordingId, Func<IReadOnlyList<AskTurn>, IReadOnlyList<AskTurn>> change, CancellationToken cancellationToken)
    {
        var gate = Gate(recordingId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var (turns, writable) = await ReadUnlockedAsync(recordingId, cancellationToken);
            if (!writable)
            {
                throw AskErrors.NewerVersion();
            }

            var next = change(turns);
            var path = PathOf(recordingId);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 16 * 1024, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, new AskConversationFile(AskConversationFile.CurrentSchemaVersion, next), AskJsonContext.Default.AskConversationFile, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            await AtomicReplace.ReplaceAsync(temporary, path, cancellationToken);
            return next;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<(IReadOnlyList<AskTurn> Turns, bool Writable)> ReadUnlockedAsync(string recordingId, CancellationToken cancellationToken)
    {
        var path = PathOf(recordingId);
        if (!File.Exists(path))
        {
            return ([], true);
        }

        try
        {
            string json;
            await using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 16 * 1024, useAsync: true))
            using (var reader = new StreamReader(stream))
            {
                json = await reader.ReadToEndAsync(cancellationToken);
            }

            using var document = JsonDocument.Parse(json);
            var version = document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("schemaVersion", out var v) && v.TryGetInt32(out var n) ? n : 0;
            if (version > AskConversationFile.CurrentSchemaVersion)
            {
                LogNewer(recordingId, version);
                return (Readable(json), false);
            }

            if (version != AskConversationFile.CurrentSchemaVersion)
            {
                throw new JsonException($"schemaVersion {version} is not a version Memento wrote");
            }

            var file = JsonSerializer.Deserialize(json, AskJsonContext.Default.AskConversationFile) ?? throw new JsonException("empty file");
            return (Clean(file.Turns), true);
        }
        catch (JsonException ex)
        {
            // Keep what was there for the person (or support) to look at, and start again.
            var aside = Path.Combine(Path.GetDirectoryName(path)!, string.Create(CultureInfo.InvariantCulture, $"conversation.damaged-{time.GetUtcNow():yyyyMMddHHmmss}.json"));
            File.Move(path, aside, overwrite: true);
            LogDamaged(ex, recordingId, Path.GetFileName(aside));
            return ([], true);
        }
    }

    /// <summary>A newer file's turns as far as this version understands them; none when it cannot.</summary>
    private static List<AskTurn> Readable(string json)
    {
        try
        {
            return JsonSerializer.Deserialize(json, AskJsonContext.Default.AskConversationFile) is { } file ? Clean(file.Turns) : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Turns written as null or without the members every turn has are left out.</summary>
    private static List<AskTurn> Clean(IReadOnlyList<AskTurn>? turns) =>
        (turns ?? []).Where(t => t is not null && !string.IsNullOrEmpty(t.Id) && t.Role is "user" or "assistant" && t.Text is not null).ToList();

    private SemaphoreSlim Gate(string recordingId) => _locks.GetOrAdd(recordingId, _ => new SemaphoreSlim(1, 1));

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Ask conversation of recording {RecordingId} was damaged; kept as {Aside} and started again")]
    private partial void LogDamaged(Exception exception, string recordingId, string aside);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Ask conversation of recording {RecordingId} has schema version {Version}, newer than this Memento; it is left as it is")]
    private partial void LogNewer(string recordingId, int version);
}
