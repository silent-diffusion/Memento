using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Library;
using Microsoft.Extensions.Logging;

namespace Memento.Core.Projects;

/// <summary>File-system <see cref="IProjectStore"/>. See the interface for the rules it keeps.</summary>
public sealed partial class ProjectStore : IProjectStore
{
    private const int MaxIdAttempts = 8;

    private readonly ILibraryLocation _library;
    private readonly TimeProvider _time;
    private readonly ILogger<ProjectStore> _logger;
    private readonly ProjectManifestMigrator _migrator;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public ProjectStore(ILibraryLocation library, TimeProvider time, ILogger<ProjectStore> logger)
        : this(library, time, logger, ProjectManifestMigrator.Default)
    {
    }

    public ProjectStore(ILibraryLocation library, TimeProvider time, ILogger<ProjectStore> logger, ProjectManifestMigrator migrator)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(migrator);
        _library = library;
        _time = time;
        _logger = logger;
        _migrator = migrator;
    }

    public string ProjectsRoot => Path.Combine(_library.Root, ProjectLayout.ProjectsFolder);

    public string GetProjectFolder(string recordingId)
    {
        if (!ProjectId.IsValid(recordingId))
        {
            throw new ProjectNotFoundException(recordingId ?? string.Empty);
        }

        return Path.Combine(ProjectsRoot, recordingId);
    }

    public bool Exists(string recordingId) =>
        ProjectId.IsValid(recordingId) && File.Exists(Path.Combine(ProjectsRoot, recordingId, ProjectLayout.ManifestFile));

    public IReadOnlyList<string> ListIds()
    {
        if (!Directory.Exists(ProjectsRoot))
        {
            return [];
        }

        var ids = new List<string>();
        foreach (var folder in Directory.EnumerateDirectories(ProjectsRoot))
        {
            var name = Path.GetFileName(folder);
            if (ProjectId.IsValid(name) && File.Exists(Path.Combine(folder, ProjectLayout.ManifestFile)))
            {
                ids.Add(name);
            }
        }

        return ids;
    }

    public async Task<ProjectManifest> CreateAsync(ProjectCreateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Directory.CreateDirectory(ProjectsRoot);
        for (var attempt = 0; attempt < MaxIdAttempts; attempt++)
        {
            var id = ProjectId.New(request.CreatedAt);
            var folder = Path.Combine(ProjectsRoot, id);
            if (Directory.Exists(folder))
            {
                continue;
            }

            Directory.CreateDirectory(folder);
            Directory.CreateDirectory(Path.Combine(folder, ProjectLayout.TracksFolder));
            Directory.CreateDirectory(Path.Combine(folder, ProjectLayout.AttachmentsFolder));
            Directory.CreateDirectory(Path.Combine(folder, ProjectLayout.VersionsFolder));

            var manifest = new ProjectManifest
            {
                Id = id,
                CreatedAt = request.CreatedAt,
                State = request.State,
                Details = new ProjectDetails { Title = request.Title, Type = request.Type },
            };

            var saved = await SaveAsync(manifest, cancellationToken);
            await AtomicJsonFile.WriteAsync(
                Path.Combine(folder, ProjectLayout.AnnotationsFile),
                new AnnotationsDocument(),
                ProjectJsonContext.Default.AnnotationsDocument,
                cancellationToken);
            LogCreated(id);
            return saved;
        }

        throw new IOException($"Memento could not find a free project folder name in {ProjectsRoot}.");
    }

    public async Task<ProjectManifest> LoadAsync(string recordingId, CancellationToken cancellationToken)
    {
        var path = Path.Combine(GetProjectFolder(recordingId), ProjectLayout.ManifestFile);
        if (!File.Exists(path))
        {
            throw new ProjectNotFoundException(recordingId);
        }

        try
        {
            var text = await AtomicJsonFile.ReadSharedTextAsync(path, cancellationToken);
            if (JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) is not JsonObject node)
            {
                throw new JsonException("project.json is not a JSON object.");
            }

            var migration = _migrator.Migrate(node);
            if (migration.Migrated)
            {
                LogMigrated(recordingId, migration.FromVersion, _migrator.CurrentVersion);
            }

            var manifest = migration.Manifest.Deserialize(ProjectJsonContext.Default.ProjectManifest)
                ?? throw new JsonException("project.json is empty.");
            if (!string.Equals(manifest.Id, recordingId, StringComparison.Ordinal))
            {
                // A copied folder: the folder name is the identity.
                manifest = manifest with { Id = recordingId };
            }

            // A copied-in folder's manifest is untrusted: every file it names must stay inside the folder.
            if (ProjectPaths.FirstUnsafe(manifest) is { } field)
            {
                LogUnsafePath(recordingId, field);
                throw new ProjectNotFoundException(
                    $"The details file of recording {recordingId} names a file outside its folder ({field}). Memento will not open it; nothing was changed.",
                    new InvalidDataException($"project.json {field} is not a file inside the project folder."));
            }

            return manifest;
        }
        catch (JsonException ex)
        {
            LogUnreadable(recordingId, ex);
            throw new ProjectNotFoundException($"The details file of recording {recordingId} could not be read.", ex);
        }
    }

    public async Task<ProjectManifest> SaveAsync(ProjectManifest manifest, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var folder = GetProjectFolder(manifest.Id);
        var gate = Lock(manifest.Id);
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await WriteManifestAsync(folder, manifest, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<ProjectManifest> UpdateAsync(string recordingId, Func<ProjectManifest, ProjectManifest> update, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);
        var folder = GetProjectFolder(recordingId);
        var gate = Lock(recordingId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var current = await LoadAsync(recordingId, cancellationToken);
            var next = update(current) with { Id = recordingId };
            return await WriteManifestAsync(folder, next, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task DeleteAsync(string recordingId, CancellationToken cancellationToken)
    {
        var folder = GetProjectFolder(recordingId);
        if (!Directory.Exists(folder))
        {
            throw new ProjectNotFoundException(recordingId);
        }

        var gate = Lock(recordingId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(Path.Combine(folder, ProjectLayout.RecordingStateFile)))
            {
                throw new ProjectBusyException(recordingId);
            }

            // Finalized tracks are read-only; clear that first or Windows refuses the delete.
            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                var attributes = File.GetAttributes(file);
                if ((attributes & FileAttributes.ReadOnly) != 0)
                {
                    File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
                }
            }

            Directory.Delete(folder, recursive: true);
            LogDeleted(recordingId);
        }
        finally
        {
            gate.Release();
        }
    }

    public long GetSizeBytes(string recordingId)
    {
        var folder = GetProjectFolder(recordingId);
        if (!Directory.Exists(folder))
        {
            throw new ProjectNotFoundException(recordingId);
        }

        long total = 0;
        foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories))
        {
            total += file.Length;
        }

        return total;
    }

    public async Task AppendHistoryAsync(string recordingId, HistoryEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var path = Path.Combine(GetProjectFolder(recordingId), ProjectLayout.HistoryFile);
        var line = JsonSerializer.SerializeToUtf8Bytes(HistoryLine.From(entry), ProjectJsonContext.Compact.HistoryLine);
        var gate = Lock(recordingId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            await using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, useAsync: true);
            await stream.WriteAsync(line, cancellationToken);
            await stream.WriteAsync("\n"u8.ToArray(), cancellationToken);
            await stream.FlushAsync(cancellationToken);
            stream.Flush(flushToDisk: true);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<HistoryEntry>> ReadHistoryAsync(string recordingId, CancellationToken cancellationToken)
    {
        var path = Path.Combine(GetProjectFolder(recordingId), ProjectLayout.HistoryFile);
        if (!File.Exists(path))
        {
            return [];
        }

        var entries = new List<HistoryEntry>();
        var lineNumber = 0;
        var text = await AtomicJsonFile.ReadSharedTextAsync(path, cancellationToken);
        foreach (var line in text.Split('\n'))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var parsed = JsonSerializer.Deserialize(line, ProjectJsonContext.Compact.HistoryLine);
                if (parsed is not null)
                {
                    entries.Add(parsed.ToEntry());
                }
            }
            catch (JsonException)
            {
                // A line cut short by a crash; the rest of the log is still good.
                LogHistoryLineSkipped(recordingId, lineNumber);
            }
        }

        return entries;
    }

    public async Task<AnnotationsDocument> LoadAnnotationsAsync(string recordingId, CancellationToken cancellationToken)
    {
        var path = Path.Combine(GetProjectFolder(recordingId), ProjectLayout.AnnotationsFile);
        try
        {
            return await AtomicJsonFile.ReadAsync(path, ProjectJsonContext.Default.AnnotationsDocument, cancellationToken)
                ?? new AnnotationsDocument();
        }
        catch (JsonException ex)
        {
            LogAnnotationsUnreadable(recordingId, ex);
            throw new ProjectSchemaException($"The chapters and highlights of recording {recordingId} could not be read.", ex);
        }
    }

    public async Task<AnnotationsDocument> UpdateAnnotationsAsync(
        string recordingId,
        Func<AnnotationsDocument, AnnotationsDocument> update,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);
        var folder = GetProjectFolder(recordingId);
        if (!File.Exists(Path.Combine(folder, ProjectLayout.ManifestFile)))
        {
            throw new ProjectNotFoundException(recordingId);
        }

        var gate = Lock(recordingId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var current = await LoadAnnotationsAsync(recordingId, cancellationToken);
            if (current.SchemaVersion > AnnotationsDocument.CurrentSchemaVersion)
            {
                throw new ProjectSchemaException($"The annotations of recording {recordingId} were saved by a newer Memento and are read-only here.");
            }

            var next = update(current) with { SchemaVersion = AnnotationsDocument.CurrentSchemaVersion };
            await AtomicJsonFile.WriteAsync(Path.Combine(folder, ProjectLayout.AnnotationsFile), next, ProjectJsonContext.Default.AnnotationsDocument, cancellationToken);
            return next;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task WriteRecordingStateAsync(RecordingStateDocument state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        var path = Path.Combine(GetProjectFolder(state.RecordingId), ProjectLayout.RecordingStateFile);
        await AtomicJsonFile.WriteAsync(
            path,
            state with { SchemaVersion = RecordingStateDocument.CurrentSchemaVersion },
            ProjectJsonContext.Default.RecordingStateDocument,
            cancellationToken);
    }

    public async Task<RecordingStateDocument?> ReadRecordingStateAsync(string recordingId, CancellationToken cancellationToken)
    {
        var path = Path.Combine(GetProjectFolder(recordingId), ProjectLayout.RecordingStateFile);
        try
        {
            var state = await AtomicJsonFile.ReadAsync(path, ProjectJsonContext.Default.RecordingStateDocument, cancellationToken);
            if (state is not null && ProjectPaths.FirstUnsafe(state) is { } field)
            {
                // Recovery rewrites the headers of these files: never one outside the project folder.
                LogUnsafePath(recordingId, "recording.state.json " + field);
                return null;
            }

            return state;
        }
        catch (JsonException ex)
        {
            LogStateUnreadable(recordingId, ex);
            return null;
        }
    }

    public bool HasRecordingState(string recordingId) =>
        File.Exists(Path.Combine(GetProjectFolder(recordingId), ProjectLayout.RecordingStateFile));

    public void DeleteRecordingState(string recordingId)
    {
        var path = Path.Combine(GetProjectFolder(recordingId), ProjectLayout.RecordingStateFile);
        File.Delete(path);
        File.Delete(path + ".tmp");
    }

    private async Task<ProjectManifest> WriteManifestAsync(string folder, ProjectManifest manifest, CancellationToken cancellationToken)
    {
        if (manifest.SchemaVersion > _migrator.CurrentVersion)
        {
            throw new ProjectSchemaException(
                $"Recording {manifest.Id} was saved by a newer version of Memento (project schema {manifest.SchemaVersion}). Update Memento to change it; nothing was written.");
        }

        var saved = manifest with
        {
            SchemaVersion = _migrator.CurrentVersion,
            ModifiedAt = NextModifiedAt(manifest.ModifiedAt),
        };
        await AtomicJsonFile.WriteAsync(Path.Combine(folder, ProjectLayout.ManifestFile), saved, ProjectJsonContext.Default.ProjectManifest, cancellationToken);
        return saved;
    }

    /// <summary>Now, but always later than the previous value, so every write is observable as newer.</summary>
    private DateTimeOffset NextModifiedAt(DateTimeOffset previous)
    {
        var now = _time.GetLocalNow();
        return now > previous ? now : previous.AddMilliseconds(1);
    }

    private SemaphoreSlim Lock(string recordingId) => _locks.GetOrAdd(recordingId, _ => new SemaphoreSlim(1, 1));

    [LoggerMessage(Level = LogLevel.Information, Message = "Created project {RecordingId}")]
    private partial void LogCreated(string recordingId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted project {RecordingId} through the Delete flow")]
    private partial void LogDeleted(string recordingId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Project {RecordingId} read from schema {From} and migrated to {To}")]
    private partial void LogMigrated(string recordingId, int from, int to);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Project {RecordingId} has an unreadable project.json")]
    private partial void LogUnreadable(string recordingId, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Project {RecordingId} has an unreadable annotations.json")]
    private partial void LogAnnotationsUnreadable(string recordingId, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Project {RecordingId} was refused: {Field} names a file outside the project folder")]
    private partial void LogUnsafePath(string recordingId, string field);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Project {RecordingId} has an unreadable recording.state.json")]
    private partial void LogStateUnreadable(string recordingId, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Project {RecordingId}: history line {Line} is damaged and was skipped")]
    private partial void LogHistoryLineSkipped(string recordingId, int line);
}
