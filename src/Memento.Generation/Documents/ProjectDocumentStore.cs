using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Memento.Core.Projects;
using Memento.Core.Settings;
using Memento.Documents.Model;
using Memento.Documents.Model.Storage;
using Microsoft.Extensions.Logging;

namespace Memento.Generation.Documents;

/// <summary>
/// The documents of each project, <c>documents/&lt;id&gt;.json</c> (ARCHITECTURE.md §4), in Memento.Documents' block model.
/// Writes are atomic and serialised per recording. When Settings › History keeps versions, the replaced content goes to
/// <c>versions/document.&lt;id&gt;.&lt;utc-stamp&gt;.json</c> by the same rules as the transcript: on the first edit after a
/// generation or a restore (a run of edits is one version), on a regeneration and on a restore; versions older than the
/// kept days are pruned.
/// </summary>
public sealed partial class ProjectDocumentStore(IProjectStore projects, ISettingsStore settings, TimeProvider time, ILogger<ProjectDocumentStore> logger)
{
    private const string VersionPrefix = "document.";
    private const string StampFormat = "yyyyMMdd'T'HHmmssfff'Z'";
    private const int MaxIdLength = 64;

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);
    private readonly ILogger<ProjectDocumentStore> _logger = logger;

    /// <summary>A new document id: <c>d</c> and ten random lower-case hex characters.</summary>
    public static string NewId() => "d" + Convert.ToHexString(RandomNumberGenerator.GetBytes(5)).ToLowerInvariant();

    /// <summary>Lower-case letters, digits and hyphens, so an id is always a safe file name.</summary>
    public static bool IsValidId(string? id) =>
        !string.IsNullOrEmpty(id)
        && id.Length <= MaxIdLength
        && (char.IsAsciiLetterLower(id[0]) || char.IsAsciiDigit(id[0]))
        && id.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-');

    /// <summary>Every readable document, newest change first. Unreadable files are skipped and logged.</summary>
    /// <exception cref="ProjectNotFoundException">The recording id is not valid.</exception>
    public async Task<IReadOnlyList<Document>> ListAsync(string recordingId, CancellationToken cancellationToken)
    {
        var folder = DocumentsFolder(recordingId);
        if (!Directory.Exists(folder))
        {
            return [];
        }

        var documents = new List<Document>();
        foreach (var path in Directory.EnumerateFiles(folder, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsValidId(Path.GetFileNameWithoutExtension(path)))
            {
                continue;
            }

            try
            {
                documents.Add(await DocumentJson.ReadFileAsync(path, cancellationToken));
            }
            catch (Exception ex) when (ex is DocumentFormatException or IOException)
            {
                LogUnreadable(ex, Path.GetFileName(path));
            }
        }

        return documents.OrderByDescending(d => d.ModifiedAt).ThenBy(d => d.Id, StringComparer.Ordinal).ToList();
    }

    /// <summary>The document, or <c>null</c> when there is none with that id.</summary>
    /// <exception cref="DocumentFormatException">The file exists but cannot be read.</exception>
    public async Task<Document?> LoadAsync(string recordingId, string documentId, CancellationToken cancellationToken)
    {
        if (!IsValidId(documentId))
        {
            return null;
        }

        var path = PathOf(recordingId, documentId);
        return File.Exists(path) ? await DocumentJson.ReadFileAsync(path, cancellationToken) : null;
    }

    /// <summary>The size of the document file in bytes (0 when it is missing).</summary>
    public long SizeOf(string recordingId, string documentId)
    {
        var file = new FileInfo(PathOf(recordingId, documentId));
        return file.Exists ? file.Length : 0;
    }

    /// <summary>
    /// Loads, changes and writes a document under the recording's lock. <paramref name="update"/> receives the current
    /// document (<c>null</c> when there is none) and returns the new one, or <c>null</c> to write nothing. The store sets
    /// the id, version, change reason and times; the replaced content is kept as a version when the rules say so.
    /// </summary>
    public async Task<Document?> WriteAsync(string recordingId, string documentId, string reason, Func<Document?, Document?> update, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (!IsValidId(documentId))
        {
            throw new ArgumentException($"'{documentId}' is not a document id.", nameof(documentId));
        }

        var gate = _locks.GetOrAdd(recordingId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var current = await LoadAsync(recordingId, documentId, cancellationToken);
            var next = update(current);
            if (next is null)
            {
                return null;
            }

            var now = time.GetLocalNow();
            var history = settings.Current.History;
            if (current is not null && history.KeepVersions && ShouldKeepVersion(current, reason))
            {
                await WriteVersionAsync(recordingId, current, now, cancellationToken);
            }

            var saved = next with
            {
                SchemaVersion = Document.CurrentSchemaVersion,
                Id = documentId,
                Version = (current?.Version ?? 0) + 1,
                CreatedAt = current?.CreatedAt ?? (next.CreatedAt == default ? now : next.CreatedAt),
                ModifiedAt = now,
                LastChange = reason == DocumentChangeReasons.Renamed && current?.LastChange is { } kept ? kept : new DocumentChange { Reason = reason, At = now },
            };
            var problems = DocumentJson.Validate(saved);
            if (problems.Count > 0)
            {
                throw new DocumentFormatException(DocumentFormatErrorCodes.Structure, $"The document could not be saved: {problems[0]} Nothing was changed.");
            }

            await DocumentJson.WriteFileAsync(PathOf(recordingId, documentId), saved, cancellationToken);
            if (history.KeepVersions)
            {
                Prune(recordingId, history.KeepDays, now);
            }

            return saved;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Deletes the document and its kept versions. Only the designed Delete flow (the UI confirms) calls this.</summary>
    /// <returns><c>false</c> when there was no such document.</returns>
    public async Task<bool> DeleteAsync(string recordingId, string documentId, CancellationToken cancellationToken)
    {
        if (!IsValidId(documentId))
        {
            return false;
        }

        var gate = _locks.GetOrAdd(recordingId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var path = PathOf(recordingId, documentId);
            if (!File.Exists(path))
            {
                return false;
            }

            File.Delete(path);
            foreach (var version in VersionFiles(recordingId, documentId))
            {
                TryDelete(version);
            }

            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Kept versions of one document, newest first.</summary>
    public async Task<IReadOnlyList<DocumentVersionFile>> ListVersionsAsync(string recordingId, string documentId, CancellationToken cancellationToken)
    {
        var versions = new List<DocumentVersionFile>();
        foreach (var file in VersionFiles(recordingId, documentId))
        {
            if (await ReadVersionAsync(file, cancellationToken) is { } version)
            {
                versions.Add(version);
            }
        }

        return versions.OrderByDescending(v => v.SavedAt).ThenByDescending(v => v.Id, StringComparer.Ordinal).ToList();
    }

    public async Task<DocumentVersionFile?> LoadVersionAsync(string recordingId, string documentId, string versionId, CancellationToken cancellationToken)
    {
        if (!IsValidId(documentId) || !IsStamp(versionId))
        {
            return null;
        }

        var path = Path.Combine(VersionsFolder(recordingId), VersionPrefix + documentId + "." + versionId + ".json");
        return File.Exists(path) ? await ReadVersionAsync(path, cancellationToken) : null;
    }

    /// <summary>
    /// BRIDGE.md M4, as for transcripts (M2 clarification 3): a version is kept on the first edit after a generation, a
    /// creation or a restore (a run of edits is one version), on regenerate and on restore; a rename keeps none.
    /// </summary>
    internal static bool ShouldKeepVersion(Document current, string reason) => reason switch
    {
        DocumentChangeReasons.Edited => current.LastChange?.Reason != DocumentChangeReasons.Edited,
        DocumentChangeReasons.Regenerated or DocumentChangeReasons.Restored or DocumentChangeReasons.Generated => true,
        _ => false,
    };

    private string DocumentsFolder(string recordingId) => Path.Combine(projects.GetProjectFolder(recordingId), ProjectLayout.DocumentsFolder);

    private string VersionsFolder(string recordingId) => Path.Combine(projects.GetProjectFolder(recordingId), ProjectLayout.VersionsFolder);

    private string PathOf(string recordingId, string documentId) => Path.Combine(DocumentsFolder(recordingId), documentId + ".json");

    private List<string> VersionFiles(string recordingId, string documentId)
    {
        var folder = VersionsFolder(recordingId);
        return IsValidId(documentId) && Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, VersionPrefix + documentId + ".*.json").Where(f => IsStamp(StampOf(f, documentId))).ToList()
            : [];
    }

    private static string StampOf(string file, string documentId) =>
        Path.GetFileNameWithoutExtension(file)[(VersionPrefix.Length + documentId.Length + 1)..];

    private static bool IsStamp(string value) =>
        DateTime.TryParseExact(value, StampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out _);

    private async Task<DocumentVersionFile?> ReadVersionAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 16 * 1024, useAsync: true);
            var version = await JsonSerializer.DeserializeAsync(stream, GenerationJsonContext.Default.DocumentVersionFile, cancellationToken);
            return version is null || version.SchemaVersion > DocumentVersionFile.CurrentSchemaVersion ? null : version;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            LogUnreadable(ex, Path.GetFileName(path));
            return null;
        }
    }

    private async Task WriteVersionAsync(string recordingId, Document current, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var folder = VersionsFolder(recordingId);
        Directory.CreateDirectory(folder);
        var stamp = now.UtcDateTime;
        string id;
        string path;
        do
        {
            id = stamp.ToString(StampFormat, CultureInfo.InvariantCulture);
            path = Path.Combine(folder, VersionPrefix + current.Id + "." + id + ".json");
            stamp = stamp.AddMilliseconds(1);
        }
        while (File.Exists(path));

        var version = new DocumentVersionFile
        {
            Id = id,
            DocumentId = current.Id,
            SavedAt = current.LastChange?.At ?? current.ModifiedAt,
            Reason = current.LastChange?.Reason ?? DocumentChangeReasons.Generated,
            Document = current,
        };
        await AtomicFile.WriteAllBytesAsync(path, JsonSerializer.SerializeToUtf8Bytes(version, GenerationJsonContext.Default.DocumentVersionFile), cancellationToken);
    }

    private void Prune(string recordingId, int keepDays, DateTimeOffset now)
    {
        var folder = VersionsFolder(recordingId);
        if (!Directory.Exists(folder))
        {
            return;
        }

        var cutoff = now.UtcDateTime - TimeSpan.FromDays(keepDays);
        foreach (var file in Directory.EnumerateFiles(folder, VersionPrefix + "*.json"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var stamp = name[(name.LastIndexOf('.') + 1)..];
            if (DateTime.TryParseExact(stamp, StampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at) && at < cutoff)
            {
                TryDelete(file);
            }
        }
    }

    private void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogUnreadable(ex, Path.GetFileName(file));
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Document file {File} could not be read or removed")]
    private partial void LogUnreadable(Exception exception, string file);
}
