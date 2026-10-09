using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Memento.Core.Projects;
using Memento.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Memento.Core.Transcripts;

/// <summary>
/// <c>transcript.json</c> of each project: atomic writes, a version counter, and (when Settings › History keeps
/// versions) the replaced content under <c>versions/transcript.&lt;utc-stamp&gt;.json</c>, pruned after
/// <see cref="HistorySettings.KeepDays"/>. Consecutive edits share one version.
/// Writes to one project's transcript are serialized.
/// </summary>
public sealed partial class TranscriptStore(IProjectStore projects, TimeProvider time, ILogger<TranscriptStore> logger)
{
    private const string VersionPrefix = "transcript.";
    private const string StampFormat = "yyyyMMdd'T'HHmmssfff'Z'";

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);
    private readonly ILogger<TranscriptStore> _logger = logger;

    public bool Exists(string recordingId) => File.Exists(PathOf(recordingId));

    /// <summary>The transcript, or <c>null</c> when there is none.</summary>
    /// <exception cref="ProjectSchemaException">It is unreadable or from a newer Memento.</exception>
    public async Task<TranscriptDocument?> LoadAsync(string recordingId, CancellationToken cancellationToken)
    {
        var path = PathOf(recordingId);
        try
        {
            var document = await AtomicJsonFile.ReadAsync(path, TranscriptJsonContext.Default.TranscriptDocument, cancellationToken);
            if (document is not null && document.SchemaVersion > TranscriptDocument.CurrentSchemaVersion)
            {
                throw new ProjectSchemaException($"The transcript of recording {recordingId} was saved by a newer Memento (schema {document.SchemaVersion}) and can't be read here.");
            }

            return document;
        }
        catch (JsonException ex)
        {
            LogUnreadable(ex, recordingId);
            throw new ProjectSchemaException($"The transcript of recording {recordingId} could not be read.", ex);
        }
    }

    /// <summary>
    /// Loads, changes and writes the transcript under its lock. <paramref name="update"/> receives the current
    /// transcript (<c>null</c> when there is none) and returns the new one, or <c>null</c> to write nothing.
    /// </summary>
    public async Task<TranscriptDocument?> UpdateAsync(
        string recordingId,
        string reason,
        HistorySettings history,
        Func<TranscriptDocument?, TranscriptDocument?> update,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);
        ArgumentNullException.ThrowIfNull(history);
        var gate = _locks.GetOrAdd(recordingId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var current = await LoadAsync(recordingId, cancellationToken);
            var next = update(current);
            if (next is null)
            {
                return null;
            }

            var now = time.GetLocalNow();
            if (current is not null && history.KeepVersions && ShouldKeepVersion(current, reason))
            {
                await WriteVersionAsync(recordingId, current, now, cancellationToken);
            }

            var change = reason == TranscriptChangeReasons.Speakers || reason == TranscriptChangeReasons.Topics
                ? current?.LastChange ?? new TranscriptChange(TranscriptChangeReasons.Transcribed, now)
                : new TranscriptChange(reason, now);
            var saved = next with
            {
                SchemaVersion = TranscriptDocument.CurrentSchemaVersion,
                Version = (current?.Version ?? next.Version) + 1,
                LastChange = change,
            };
            await AtomicJsonFile.WriteAsync(PathOf(recordingId), saved, TranscriptJsonContext.Default.TranscriptDocument, cancellationToken);
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

    /// <summary>Kept versions, newest first.</summary>
    public async Task<IReadOnlyList<TranscriptVersionFile>> ListVersionsAsync(string recordingId, CancellationToken cancellationToken)
    {
        var folder = VersionsFolder(recordingId);
        if (!Directory.Exists(folder))
        {
            return [];
        }

        var versions = new List<TranscriptVersionFile>();
        foreach (var file in Directory.EnumerateFiles(folder, VersionPrefix + "*.json"))
        {
            try
            {
                if (await AtomicJsonFile.ReadAsync(file, TranscriptJsonContext.Default.TranscriptVersionFile, cancellationToken) is { } version)
                {
                    versions.Add(version);
                }
            }
            catch (JsonException ex)
            {
                LogVersionUnreadable(ex, Path.GetFileName(file));
            }
        }

        return versions.OrderByDescending(v => v.SavedAt).ToList();
    }

    public async Task<TranscriptVersionFile?> LoadVersionAsync(string recordingId, string versionId, CancellationToken cancellationToken)
    {
        if (!IsVersionId(versionId))
        {
            return null;
        }

        var path = Path.Combine(VersionsFolder(recordingId), VersionPrefix + versionId + ".json");
        try
        {
            return await AtomicJsonFile.ReadAsync(path, TranscriptJsonContext.Default.TranscriptVersionFile, cancellationToken);
        }
        catch (JsonException ex)
        {
            LogVersionUnreadable(ex, Path.GetFileName(path));
            return null;
        }
    }

    public async Task<TranscriptPartial?> LoadPartialAsync(string recordingId, CancellationToken cancellationToken)
    {
        try
        {
            var partial = await AtomicJsonFile.ReadAsync(PartialPath(recordingId), TranscriptJsonContext.Default.TranscriptPartial, cancellationToken);
            return partial?.SchemaVersion == TranscriptPartial.CurrentSchemaVersion ? partial : null;
        }
        catch (JsonException ex)
        {
            LogUnreadable(ex, recordingId);
            return null;
        }
    }

    public Task SavePartialAsync(string recordingId, TranscriptPartial partial, CancellationToken cancellationToken) =>
        AtomicJsonFile.WriteAsync(PartialPath(recordingId), partial, TranscriptJsonContext.Default.TranscriptPartial, cancellationToken);

    public void DeletePartial(string recordingId)
    {
        var path = PartialPath(recordingId);
        File.Delete(path);
        File.Delete(path + ".tmp");
    }

    public async Task<SpeakersPartial?> LoadSpeakersPartialAsync(string recordingId, CancellationToken cancellationToken)
    {
        try
        {
            var partial = await AtomicJsonFile.ReadAsync(SpeakersPartialPath(recordingId), TranscriptJsonContext.Default.SpeakersPartial, cancellationToken);
            return partial?.SchemaVersion == SpeakersPartial.CurrentSchemaVersion ? partial : null;
        }
        catch (JsonException ex)
        {
            LogUnreadable(ex, recordingId);
            return null;
        }
    }

    public Task SaveSpeakersPartialAsync(string recordingId, SpeakersPartial partial, CancellationToken cancellationToken) =>
        AtomicJsonFile.WriteAsync(SpeakersPartialPath(recordingId), partial, TranscriptJsonContext.Default.SpeakersPartial, cancellationToken);

    public void DeleteSpeakersPartial(string recordingId)
    {
        var path = SpeakersPartialPath(recordingId);
        File.Delete(path);
        File.Delete(path + ".tmp");
    }

    /// <summary><c>voices.json</c>, or <c>null</c> when there is none or it cannot be read (Review then falls back to talk time).</summary>
    public async Task<VoicesDocument?> LoadVoicesAsync(string recordingId, CancellationToken cancellationToken)
    {
        try
        {
            var voices = await AtomicJsonFile.ReadAsync(VoicesPath(recordingId), TranscriptJsonContext.Default.VoicesDocument, cancellationToken);
            return voices?.SchemaVersion == VoicesDocument.CurrentSchemaVersion && voices.Clusters is not null && voices.Tracks is not null ? voices : null;
        }
        catch (JsonException ex)
        {
            LogUnreadable(ex, recordingId);
            return null;
        }
    }

    public Task SaveVoicesAsync(string recordingId, VoicesDocument voices, CancellationToken cancellationToken) =>
        AtomicJsonFile.WriteAsync(VoicesPath(recordingId), voices, TranscriptJsonContext.Default.VoicesDocument, cancellationToken);

    private string VoicesPath(string recordingId) => Path.Combine(projects.GetProjectFolder(recordingId), ProjectLayout.VoicesFile);

    private static bool IsVersionId(string versionId) =>
        DateTime.TryParseExact(versionId, StampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out _);

    /// <summary>
    /// BRIDGE.md M2 clarification 3: a version is kept on the first edit after a pass or a restore (a run of edits is one
    /// version), on retranscribe and on restore, and before a speakers pass replaces lines a person edited (after 1.2.0);
    /// a pass, a speakers pass over an unedited transcript or topics keep none.
    /// </summary>
    private static bool ShouldKeepVersion(TranscriptDocument current, string reason) => reason switch
    {
        TranscriptChangeReasons.Edited => current.LastChange?.Reason != TranscriptChangeReasons.Edited,
        // Identifying speakers again over lines a person corrected keeps those corrections as a version.
        TranscriptChangeReasons.Speakers => current.LastChange?.Reason == TranscriptChangeReasons.Edited,
        TranscriptChangeReasons.Retranscribed or TranscriptChangeReasons.Restored => true,
        _ => false,
    };

    private string PathOf(string recordingId) => Path.Combine(projects.GetProjectFolder(recordingId), ProjectLayout.TranscriptFile);

    private string PartialPath(string recordingId) => Path.Combine(projects.GetProjectFolder(recordingId), ProjectLayout.TranscriptPartialFile);

    private string SpeakersPartialPath(string recordingId) => Path.Combine(projects.GetProjectFolder(recordingId), ProjectLayout.SpeakersPartialFile);

    private string VersionsFolder(string recordingId) => Path.Combine(projects.GetProjectFolder(recordingId), ProjectLayout.VersionsFolder);

    private async Task WriteVersionAsync(string recordingId, TranscriptDocument current, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var folder = VersionsFolder(recordingId);
        Directory.CreateDirectory(folder);
        var stamp = now.UtcDateTime;
        string id;
        string path;
        do
        {
            id = stamp.ToString(StampFormat, CultureInfo.InvariantCulture);
            path = Path.Combine(folder, VersionPrefix + id + ".json");
            stamp = stamp.AddMilliseconds(1);
        }
        while (File.Exists(path));

        var reason = current.LastChange?.Reason ?? TranscriptChangeReasons.Transcribed;
        var savedAt = current.LastChange?.At ?? now;
        await AtomicJsonFile.WriteAsync(
            path,
            new TranscriptVersionFile(TranscriptVersionFile.CurrentSchemaVersion, id, savedAt, reason, current),
            TranscriptJsonContext.Default.TranscriptVersionFile,
            cancellationToken);
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
            var id = Path.GetFileNameWithoutExtension(file)[VersionPrefix.Length..];
            if (DateTime.TryParseExact(id, StampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at) && at < cutoff)
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    LogVersionUnreadable(ex, Path.GetFileName(file));
                }
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The transcript of recording {RecordingId} could not be read")]
    private partial void LogUnreadable(Exception exception, string recordingId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Transcript version {File} could not be read or pruned")]
    private partial void LogVersionUnreadable(Exception exception, string file);
}
