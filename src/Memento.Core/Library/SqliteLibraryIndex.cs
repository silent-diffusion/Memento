using System.Globalization;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Projects;
using Memento.Core.Transcripts;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Memento.Core.Library;

/// <summary>
/// <see cref="ILibraryIndex"/> on SQLite (WAL journal, FTS5 over title, people and transcript text). The transcript
/// text and renamed speakers live in their own table, written only when a transcript changes, so manifest writes stay
/// cheap. Connections are opened per call without pooling, so the file can always be set aside or rebuilt.
/// </summary>
public sealed partial class SqliteLibraryIndex : ILibraryIndex, IDisposable
{
    public const string DatabaseFileName = "library.db";
    public const int SchemaVersion = 2;

    private const int SqliteCorrupt = 11;
    private const int SqliteNotADatabase = 26;

    private const string Schema = """
        CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS recordings (
            id TEXT PRIMARY KEY,
            title TEXT NOT NULL,
            type TEXT NOT NULL,
            createdAt TEXT NOT NULL,
            createdAtUtc INTEGER NOT NULL,
            durationMs INTEGER NOT NULL,
            participantCount INTEGER NOT NULL,
            hasVideo INTEGER NOT NULL,
            state TEXT NOT NULL,
            isProcessing INTEGER NOT NULL,
            sizeBytes INTEGER NOT NULL,
            modifiedAt TEXT NOT NULL);
        CREATE INDEX IF NOT EXISTS ix_recordings_createdAtUtc ON recordings (createdAtUtc);
        CREATE TABLE IF NOT EXISTS people (
            recordingId TEXT NOT NULL REFERENCES recordings (id) ON DELETE CASCADE,
            ordinal INTEGER NOT NULL,
            name TEXT NOT NULL,
            PRIMARY KEY (recordingId, ordinal));
        CREATE TABLE IF NOT EXISTS stages (
            recordingId TEXT NOT NULL REFERENCES recordings (id) ON DELETE CASCADE,
            ordinal INTEGER NOT NULL,
            stage TEXT NOT NULL,
            state TEXT NOT NULL,
            percent INTEGER,
            label TEXT,
            PRIMARY KEY (recordingId, stage));
        CREATE TABLE IF NOT EXISTS transcripts (
            recordingId TEXT PRIMARY KEY,
            text TEXT NOT NULL,
            speakers TEXT NOT NULL);
        CREATE VIRTUAL TABLE IF NOT EXISTS recordings_fts USING fts5 (
            id UNINDEXED, title, people, transcript, tokenize = 'unicode61 remove_diacritics 2');
        """;

    private const string SelectColumns =
        "r.id, r.title, r.type, r.createdAt, r.durationMs, r.hasVideo, r.state, r.sizeBytes";

    private readonly ILibraryLocation _library;
    private readonly IProjectStore _store;
    private readonly TimeProvider _time;
    private readonly ILogger<SqliteLibraryIndex> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TranscriptStore? _transcripts;
    private string? _databasePath;

    public SqliteLibraryIndex(ILibraryLocation library, IProjectStore store, TimeProvider time, ILogger<SqliteLibraryIndex> logger, TranscriptStore? transcripts = null)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        _library = library;
        _store = store;
        _time = time;
        _logger = logger;
        _transcripts = transcripts;
    }

    /// <summary>Where the database lives: <c>&lt;library&gt;\library.db</c>.</summary>
    public string DatabasePath => Path.Combine(_library.Root, DatabaseFileName);

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedLockedAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpsertAsync(ProjectManifest manifest, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedLockedAsync(cancellationToken);
            await using var connection = await OpenAsync(cancellationToken);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            await UpsertCoreAsync(connection, transaction, manifest, SizeOf(manifest.Id), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveAsync(string recordingId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedLockedAsync(cancellationToken);
            await using var connection = await OpenAsync(cancellationToken);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            await DeleteRowsAsync(connection, transaction, recordingId, cancellationToken);
            await using (var transcript = connection.CreateCommand())
            {
                transcript.Transaction = transaction;
                transcript.CommandText = "DELETE FROM transcripts WHERE recordingId = $id";
                transcript.Parameters.AddWithValue("$id", recordingId);
                await transcript.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetTranscriptAsync(string recordingId, string text, IReadOnlyList<string> renamedSpeakers, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(renamedSpeakers);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedLockedAsync(cancellationToken);
            await using var connection = await OpenAsync(cancellationToken);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            await SetTranscriptCoreAsync(connection, transaction, recordingId, text, renamedSpeakers, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<LibraryQueryResult> QueryAsync(LibraryQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        await InitializeAsync(cancellationToken);
        var match = FtsQuery.Build(query.Text);
        var type = string.IsNullOrWhiteSpace(query.Type) || query.Type == "all" ? null : query.Type;
        var order = query.Sort switch
        {
            LibrarySort.Oldest => "r.createdAtUtc ASC, r.id ASC",
            LibrarySort.Longest => "r.durationMs DESC, r.createdAtUtc DESC",
            LibrarySort.Title => "r.title COLLATE NOCASE ASC, r.createdAtUtc DESC",
            LibrarySort.Size => "r.sizeBytes DESC, r.createdAtUtc DESC",
            _ => "r.createdAtUtc DESC, r.id DESC",
        };

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {SelectColumns} FROM recordings r
            WHERE r.state <> 'recording'
              AND ($type IS NULL OR r.type = $type)
              AND ($match IS NULL OR r.id IN (SELECT id FROM recordings_fts WHERE recordings_fts MATCH $match))
            ORDER BY {order}
            """;
        command.Parameters.AddWithValue("$type", (object?)type ?? DBNull.Value);
        command.Parameters.AddWithValue("$match", (object?)match ?? DBNull.Value);
        var rows = await ReadRowsAsync(command, cancellationToken);
        var recordings = await AttachDetailsAsync(connection, rows, cancellationToken);
        var snippets = match is null || rows.Count == 0
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : await ReadSnippetsAsync(connection, query.Text, cancellationToken);
        return new LibraryQueryResult(
            recordings.Select(r => snippets.TryGetValue(r.Summary.Id, out var snippet) ? r.Summary with { MatchSnippet = snippet } : r.Summary).ToList(),
            recordings.Sum(r => r.Summary.DurationMs),
            recordings.Count);
    }

    public async Task<IReadOnlyList<ProcessingEntry>> ListProcessingAsync(CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM recordings r WHERE r.isProcessing = 1 ORDER BY r.createdAtUtc DESC, r.id DESC";
        var rows = await ReadRowsAsync(command, cancellationToken);
        return await AttachDetailsAsync(connection, rows, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ListIdsByStateAsync(string state, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id FROM recordings WHERE state = $state ORDER BY createdAtUtc DESC";
        command.Parameters.AddWithValue("$state", state);
        var ids = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            ids.Add(reader.GetString(0));
        }

        return ids;
    }

    public async Task<int> RebuildAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedLockedAsync(cancellationToken);
            return await RebuildLockedAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private async Task EnsureInitializedLockedAsync(CancellationToken cancellationToken)
    {
        var path = DatabasePath;
        if (_databasePath == path)
        {
            return;
        }

        Directory.CreateDirectory(_library.Root);
        var needsRebuild = !File.Exists(path);
        try
        {
            needsRebuild |= !await PrepareAsync(path, cancellationToken);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode is SqliteCorrupt or SqliteNotADatabase)
        {
            SetAside(path, ex.Message);
            needsRebuild = true;
            await PrepareAsync(path, cancellationToken);
        }
        catch (InvalidDataException ex)
        {
            SetAside(path, ex.Message);
            needsRebuild = true;
            await PrepareAsync(path, cancellationToken);
        }

        _databasePath = path;
        if (needsRebuild)
        {
            var count = await RebuildLockedAsync(cancellationToken);
            LogRebuilt(path, count);
        }
    }

    /// <summary>Checks integrity and creates the schema; returns <c>false</c> when the schema had to be created.</summary>
    /// <exception cref="InvalidDataException">The integrity check found damage.</exception>
    private static async Task<bool> PrepareAsync(string path, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(path, cancellationToken);
        await using (var check = connection.CreateCommand())
        {
            check.CommandText = "PRAGMA quick_check";
            var result = (string?)await check.ExecuteScalarAsync(cancellationToken);
            if (!string.Equals(result, "ok", StringComparison.Ordinal))
            {
                throw new InvalidDataException($"The library index failed its integrity check: {result}");
            }
        }

        bool existed;
        await using (var probe = connection.CreateCommand())
        {
            probe.CommandText = "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name = 'meta'";
            existed = Convert.ToInt64(await probe.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) > 0;
        }

        if (existed)
        {
            await using var version = connection.CreateCommand();
            version.CommandText = "SELECT value FROM meta WHERE key = 'schemaVersion'";
            if (!string.Equals((string?)await version.ExecuteScalarAsync(cancellationToken), SchemaVersion.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
            {
                // The index is a cache: a different schema is dropped and rebuilt from the project folders.
                await using var drop = connection.CreateCommand();
                drop.CommandText = "DROP TABLE IF EXISTS people; DROP TABLE IF EXISTS stages; DROP TABLE IF EXISTS recordings; DROP TABLE IF EXISTS recordings_fts; DROP TABLE IF EXISTS transcripts; DROP TABLE IF EXISTS meta;";
                await drop.ExecuteNonQueryAsync(cancellationToken);
                existed = false;
            }
        }

        await using (var create = connection.CreateCommand())
        {
            create.CommandText = Schema + $"\nINSERT OR IGNORE INTO meta (key, value) VALUES ('schemaVersion', '{SchemaVersion}');";
            await create.ExecuteNonQueryAsync(cancellationToken);
        }

        return existed;
    }

    private async Task<int> RebuildLockedAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using (var clear = connection.CreateCommand())
        {
            clear.Transaction = transaction;
            clear.CommandText = "DELETE FROM people; DELETE FROM stages; DELETE FROM recordings; DELETE FROM recordings_fts; DELETE FROM transcripts;";
            await clear.ExecuteNonQueryAsync(cancellationToken);
        }

        var count = 0;
        foreach (var id in _store.ListIds())
        {
            try
            {
                var manifest = await _store.LoadAsync(id, cancellationToken);
                await UpsertCoreAsync(connection, transaction, manifest, SizeOf(manifest.Id), cancellationToken);
                if (_transcripts is not null && await LoadTranscriptQuietlyAsync(id, cancellationToken) is { } transcript)
                {
                    await SetTranscriptCoreAsync(connection, transaction, id, transcript.IndexText(), transcript.RenamedSpeakers(), cancellationToken);
                }

                count++;
            }
            catch (ProjectNotFoundException)
            {
                LogSkipped(id);
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return count;
    }

    private static async Task UpsertCoreAsync(SqliteConnection connection, SqliteTransaction transaction, ProjectManifest manifest, long sizeBytes, CancellationToken cancellationToken)
    {
        await DeleteRowsAsync(connection, transaction, manifest.Id, cancellationToken);
        var people = manifest.Details.Participants;
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO recordings (id, title, type, createdAt, createdAtUtc, durationMs, participantCount, hasVideo, state, isProcessing, sizeBytes, modifiedAt)
                VALUES ($id, $title, $type, $createdAt, $createdAtUtc, $durationMs, $participantCount, $hasVideo, $state, $isProcessing, $sizeBytes, $modifiedAt);
                INSERT INTO recordings_fts (id, title, people, transcript) VALUES (
                    $id,
                    $title,
                    $people || ' ' || COALESCE((SELECT replace(speakers, char(10), ' ') FROM transcripts WHERE recordingId = $id), ''),
                    COALESCE((SELECT text FROM transcripts WHERE recordingId = $id), ''));
                """;
            insert.Parameters.AddWithValue("$id", manifest.Id);
            insert.Parameters.AddWithValue("$title", manifest.Details.Title);
            insert.Parameters.AddWithValue("$type", manifest.Details.Type);
            insert.Parameters.AddWithValue("$createdAt", manifest.CreatedAt.ToString("o", CultureInfo.InvariantCulture));
            insert.Parameters.AddWithValue("$createdAtUtc", manifest.CreatedAt.UtcTicks);
            insert.Parameters.AddWithValue("$durationMs", manifest.DurationMs);
            insert.Parameters.AddWithValue("$participantCount", people.Count);
            insert.Parameters.AddWithValue("$hasVideo", manifest.HasVideo ? 1 : 0);
            insert.Parameters.AddWithValue("$state", manifest.State);
            insert.Parameters.AddWithValue("$isProcessing", ProjectMapper.IsProcessing(manifest.Stages) ? 1 : 0);
            insert.Parameters.AddWithValue("$sizeBytes", sizeBytes);
            insert.Parameters.AddWithValue("$modifiedAt", manifest.ModifiedAt.ToString("o", CultureInfo.InvariantCulture));
            insert.Parameters.AddWithValue("$people", string.Join(' ', people));
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        for (var i = 0; i < people.Count; i++)
        {
            await using var person = connection.CreateCommand();
            person.Transaction = transaction;
            person.CommandText = "INSERT INTO people (recordingId, ordinal, name) VALUES ($id, $ordinal, $name)";
            person.Parameters.AddWithValue("$id", manifest.Id);
            person.Parameters.AddWithValue("$ordinal", i);
            person.Parameters.AddWithValue("$name", people[i]);
            await person.ExecuteNonQueryAsync(cancellationToken);
        }

        for (var i = 0; i < manifest.Stages.Count; i++)
        {
            var stage = manifest.Stages[i];
            await using var row = connection.CreateCommand();
            row.Transaction = transaction;
            row.CommandText = "INSERT OR REPLACE INTO stages (recordingId, ordinal, stage, state, percent, label) VALUES ($id, $ordinal, $stage, $state, $percent, $label)";
            row.Parameters.AddWithValue("$id", manifest.Id);
            row.Parameters.AddWithValue("$ordinal", i);
            row.Parameters.AddWithValue("$stage", stage.Stage);
            row.Parameters.AddWithValue("$state", stage.State);
            row.Parameters.AddWithValue("$percent", (object?)stage.Percent ?? DBNull.Value);
            row.Parameters.AddWithValue("$label", (object?)stage.Label ?? DBNull.Value);
            await row.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task DeleteRowsAsync(SqliteConnection connection, SqliteTransaction transaction, string recordingId, CancellationToken cancellationToken)
    {
        await using var delete = connection.CreateCommand();
        delete.Transaction = transaction;
        delete.CommandText = """
            DELETE FROM people WHERE recordingId = $id;
            DELETE FROM stages WHERE recordingId = $id;
            DELETE FROM recordings WHERE id = $id;
            DELETE FROM recordings_fts WHERE id = $id;
            """;
        delete.Parameters.AddWithValue("$id", recordingId);
        await delete.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<List<Row>> ReadRowsAsync(SqliteCommand command, CancellationToken cancellationToken)
    {
        var rows = new List<Row>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new Row(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                reader.GetInt64(4),
                reader.GetInt64(5) != 0,
                reader.GetString(6),
                reader.GetInt64(7)));
        }

        return rows;
    }

    private static async Task<List<ProcessingEntry>> AttachDetailsAsync(SqliteConnection connection, List<Row> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        // A library of thousands of recordings has tens of thousands of these rows at most; read them in one pass.
        var people = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT recordingId, name FROM people ORDER BY recordingId, ordinal";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var id = reader.GetString(0);
                if (!people.TryGetValue(id, out var list))
                {
                    people[id] = list = [];
                }

                list.Add(reader.GetString(1));
            }
        }

        // Renamed speakers count as people for search and the meta line (BRIDGE.md: participants + renamed speakers).
        var speakers = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT recordingId, speakers FROM transcripts WHERE speakers <> ''";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                speakers[reader.GetString(0)] = reader.GetString(1).Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
            }
        }

        var stages = new Dictionary<string, List<StageStatus>>(StringComparer.Ordinal);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT recordingId, stage, state, percent, label FROM stages ORDER BY recordingId, ordinal";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var id = reader.GetString(0);
                if (!stages.TryGetValue(id, out var list))
                {
                    stages[id] = list = [];
                }

                list.Add(new StageStatus(
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetInt32(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4)));
            }
        }

        var result = new List<ProcessingEntry>(rows.Count);
        foreach (var row in rows)
        {
            IReadOnlyList<string> rowPeople = people.TryGetValue(row.Id, out var p) ? p : [];
            IReadOnlyList<StageStatus> rowStages = stages.TryGetValue(row.Id, out var s) ? s : [];
            var summary = ProjectMapper.BuildSummary(row.Id, row.Title, row.Type, row.CreatedAt, row.DurationMs, row.HasVideo, rowPeople, rowStages, row.State, row.SizeBytes);
            if (speakers.TryGetValue(row.Id, out var named))
            {
                summary = summary with { People = rowPeople.Concat(named.Where(n => !rowPeople.Contains(n, StringComparer.OrdinalIgnoreCase))).ToList() };
            }
            result.Add(new ProcessingEntry(summary, rowStages));
        }

        return result;
    }

    private static async Task SetTranscriptCoreAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string recordingId,
        string text,
        IReadOnlyList<string> renamedSpeakers,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO transcripts (recordingId, text, speakers) VALUES ($id, $text, $speakers)
                ON CONFLICT (recordingId) DO UPDATE SET text = excluded.text, speakers = excluded.speakers;
            DELETE FROM recordings_fts WHERE id = $id;
            INSERT INTO recordings_fts (id, title, people, transcript)
                SELECT r.id, r.title,
                       COALESCE((SELECT group_concat(name, ' ') FROM people WHERE recordingId = r.id), '') || ' ' || $speakersFlat,
                       $text
                FROM recordings r WHERE r.id = $id;
            """;
        command.Parameters.AddWithValue("$id", recordingId);
        command.Parameters.AddWithValue("$text", text);
        command.Parameters.AddWithValue("$speakers", string.Join('\n', renamedSpeakers));
        command.Parameters.AddWithValue("$speakersFlat", string.Join(' ', renamedSpeakers));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>For rows whose transcript matched any searched word: the words around the first match.</summary>
    private static async Task<Dictionary<string, string>> ReadSnippetsAsync(SqliteConnection connection, string? text, CancellationToken cancellationToken)
    {
        var snippets = new Dictionary<string, string>(StringComparer.Ordinal);
        if (FtsQuery.BuildColumnAny(text, "transcript") is not { } match)
        {
            return snippets;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, snippet(recordings_fts, 3, '', '', '…', 14) FROM recordings_fts WHERE recordings_fts MATCH $match";
        command.Parameters.AddWithValue("$match", match);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!reader.IsDBNull(1) && reader.GetString(1).Trim() is { Length: > 0 } snippet)
            {
                snippets[reader.GetString(0)] = snippet;
            }
        }

        return snippets;
    }

    private async Task<TranscriptDocument?> LoadTranscriptQuietlyAsync(string recordingId, CancellationToken cancellationToken)
    {
        try
        {
            return await _transcripts!.LoadAsync(recordingId, cancellationToken);
        }
        catch (ProjectSchemaException)
        {
            LogSkipped(recordingId);
            return null;
        }
    }

    private long SizeOf(string recordingId)
    {
        try
        {
            return _store.GetSizeBytes(recordingId);
        }
        catch (Exception ex) when (ex is ProjectNotFoundException or IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken) =>
        OpenAsync(_databasePath ?? DatabasePath, cancellationToken);

    private static async Task<SqliteConnection> OpenAsync(string path, CancellationToken cancellationToken)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
            DefaultTimeout = 30,
        }.ToString();
        var connection = new SqliteConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using var pragma = connection.CreateCommand();
            pragma.CommandText = "PRAGMA journal_mode = WAL; PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
            await pragma.ExecuteNonQueryAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private void SetAside(string path, string reason)
    {
        var stamp = _time.GetLocalNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var target = $"{path}.corrupt-{stamp}";
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            if (File.Exists(path + suffix))
            {
                File.Move(path + suffix, target + suffix, overwrite: true);
            }
        }

        LogSetAside(path, target, reason);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Library index {Path} was damaged ({Reason}); kept as {SetAsidePath} and rebuilt from the project folders")]
    private partial void LogSetAside(string path, string setAsidePath, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Library index {Path} rebuilt from {Count} project folders")]
    private partial void LogRebuilt(string path, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Project {RecordingId} was left out of the library index because its project.json could not be read")]
    private partial void LogSkipped(string recordingId);

    private sealed record Row(string Id, string Title, string Type, DateTimeOffset CreatedAt, long DurationMs, bool HasVideo, string State, long SizeBytes);
}
