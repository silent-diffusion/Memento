using System.Collections.Concurrent;
using System.Globalization;
using Memento.Core.Audio;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Formatting;
using Memento.Core.Host;
using Memento.Core.Library;
using Memento.Core.Maintenance;
using Memento.Core.Processing;
using Memento.Core.Projects;
using Memento.Core.Recording;
using Memento.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Memento.Core.Import;

/// <summary>
/// <c>library.importMedia</c> (BRIDGE.md M3): an existing audio or video file becomes a recording with one
/// <c>imported</c> track. The audio is decoded to a 24-bit WAV in the new project, then stored like a recording
/// (lossless FLAC, mix, peaks, SHA-256) and the normal stages are queued. The original file is never copied or
/// changed: History records its path and SHA-256. Progress is the <c>stored</c> stage's <c>processing.progress</c>.
/// </summary>
public sealed partial class MediaImportService(
    IProjectStore store,
    ProjectCatalog catalog,
    ILibraryIndex index,
    IMediaDecoder decoder,
    ProjectFinalizationService finalization,
    ProcessingOrchestrator processing,
    BridgeEventPublisher publisher,
    IFilePicker picker,
    ISettingsStore settings,
    LibraryActivity activity,
    TimeProvider time,
    IFreeSpaceProbe freeSpace,
    ILogger<MediaImportService> logger) : IAsyncDisposable, IDisposable
{
    public const string TrackId = "imported";

    /// <summary>Limits on what a file may declare before it is decoded (security audit 2026-10-07, SA-27).</summary>
    public const int MaxChannels = 8;

    public const int MaxSampleRate = 192_000;

    public const long MaxDurationMs = 24L * 3_600_000;

    /// <summary>Free space kept for recordings after an import's files: 2 GB.</summary>
    public const long ImportReserveBytes = 2L * 1024 * 1024 * 1024;

    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Source id and kind of an imported track (M3 clarification 6).</summary>
    public const string SourceId = "imported";

    public const string SourceKind = "imported";

    /// <summary>Remedy id of "Import again" (<c>processing.retry</c> with <c>stage: stored</c>).</summary>
    public const string ImportAgainRemedy = "importAgain";

    /// <summary>The failed <c>stored</c> stage's label; the UI reads it as "Import interrupted · Import again".</summary>
    public const string InterruptedLabel = "Import interrupted";

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".m4v", ".mov", ".wmv", ".avi", ".mkv", ".webm", ".3gp", ".mpg", ".mpeg", ".ts",
    };

    private readonly ConcurrentDictionary<string, Task> _running = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _closing = new();
    private readonly ILogger<MediaImportService> _logger = logger;
    private int _disposed;

    /// <summary>How long closing Memento waits for an import to notice it was cancelled.</summary>
    private static readonly TimeSpan ShutdownWait = TimeSpan.FromSeconds(10);

    public static IReadOnlyList<FileFilter> PickerFilters { get; } =
    [
        new("Audio and video", ["*.wav", "*.flac", "*.mp3", "*.m4a", "*.aac", "*.wma", "*.ogg", "*.opus", "*.mp4", "*.m4v", "*.mov", "*.wmv", "*.avi", "*.mkv", "*.webm"]),
        new("Audio files", ["*.wav", "*.flac", "*.mp3", "*.m4a", "*.aac", "*.wma", "*.ogg", "*.opus"]),
        new("Video files (audio only)", ["*.mp4", "*.m4v", "*.mov", "*.wmv", "*.avi", "*.mkv", "*.webm"]),
        new("All files", ["*.*"]),
    ];

    /// <summary>Imports still decoding or being stored.</summary>
    public bool IsBusy => !_running.IsEmpty;

    public async Task<LibraryImportMediaResult> ImportAsync(LibraryImportMediaParams parameters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        activity.ThrowIfMoving();
        var title = parameters.Title?.Trim();
        if (title is { Length: > ProjectService.MaxTitleLength })
        {
            throw M3Errors.Invalid($"A title needs 1 to {ProjectService.MaxTitleLength} characters.");
        }

        var type = parameters.Type?.Trim();
        if (type is { Length: 0 or > ProjectTypes.MaxLength })
        {
            throw M3Errors.Invalid($"A recording type needs a name of 1 to {ProjectTypes.MaxLength} characters, such as \"interview\".");
        }

        var path = parameters.Path;
        if (path is null)
        {
            path = await picker.PickFileAsync("Import audio or video", PickerFilters, cancellationToken);
            if (path is null)
            {
                return new LibraryImportMediaResult(null, Cancelled: true);
            }
        }

        M3Errors.RequireFile(path, "file to import");
        var name = Path.GetFileName(path);
        var probe = await ProbeAsync(path, name, cancellationToken);
        var modified = new DateTimeOffset(File.GetLastWriteTime(path));
        var manifest = await store.CreateAsync(
            new ProjectCreateRequest(
                string.IsNullOrEmpty(title) ? FileTitle(name) : title,
                string.IsNullOrEmpty(type) ? settings.Current.Recording.DefaultType : type,
                modified,
                ProjectStates.Finalizing),
            cancellationToken);
        await StartAsync(manifest.Id, path, name, probe, modified, cancellationToken);
        return new LibraryImportMediaResult(manifest.Id, Cancelled: false);
    }

    /// <summary>
    /// "Import again" (<c>processing.retry</c> of <c>stored</c> on an import that was cut short): decodes the same file
    /// into the same project, from the start. The project keeps its title, details and History.
    /// </summary>
    /// <exception cref="BridgeException">The project is not a failed import, or its file is gone or unreadable.</exception>
    public async Task ImportAgainAsync(string recordingId, CancellationToken cancellationToken)
    {
        activity.ThrowIfMoving();
        var manifest = await store.LoadAsync(recordingId, cancellationToken);
        if (manifest.ImportedFrom is not { } source || manifest.State != ProjectStates.Failed || _running.ContainsKey(recordingId))
        {
            throw M3Errors.Invalid($"\"{manifest.Details.Title}\" is not an import that stopped, so there is nothing to import again. Nothing was changed.");
        }

        if (!File.Exists(source.Path))
        {
            throw M3Errors.Invalid(
                $"\"{source.Name}\" is no longer at {source.Path}, so it can't be imported again. Nothing was changed. Put the file back, or delete this recording and import the file from where it is now.",
                source.Name);
        }

        var probe = await ProbeAsync(source.Path, source.Name, cancellationToken);
        DeleteDecodedTracks(recordingId);
        await AppendAsync(recordingId, new HistoryEntry(time.GetLocalNow(), "recorded", "info", "Importing again", $"From {source.Path}."), cancellationToken);
        await StartAsync(recordingId, source.Path, source.Name, probe, manifest.CreatedAt, cancellationToken);
    }

    /// <summary>
    /// At launch (<see cref="Recovery.RecoveryService"/>): an import that was still decoding or storing when Memento
    /// stopped is marked <c>failed</c> with a History note and the "Import again" remedy; its half-decoded copy is
    /// removed. The original file was never changed. Returns false when <paramref name="manifest"/> is not such an import.
    /// </summary>
    public async Task<bool> MarkInterruptedAsync(ProjectManifest manifest, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.ImportedFrom is not { } source || manifest.State != ProjectStates.Finalizing || _running.ContainsKey(manifest.Id))
        {
            return false;
        }

        DeleteDecodedTracks(manifest.Id);
        var failure = new ProjectStageFailure(
            StageNames.Stored,
            $"Importing {source.Name} stopped because Memento closed before it had finished.",
            $"The original file was not changed ({source.Path}). The half-imported copy was removed.",
            [new Remedy(ImportAgainRemedy, "Import again")],
            ProjectStageFailure.CauseCrashed,
            time.GetLocalNow());
        await catalog.UpdateAsync(
            manifest.Id,
            m => m with
            {
                State = ProjectStates.Failed,
                Stages = [new StageStatus(StageNames.Stored, StageStates.Failed, null, InterruptedLabel)],
                Failures = [.. m.Failures.Where(f => f.Stage != StageNames.Stored), failure],
            },
            cancellationToken);
        await AppendAsync(
            manifest.Id,
            new HistoryEntry(time.GetLocalNow(), "recorded", "failed", "Import interrupted", failure.Message + " " + failure.Kept + " Use Import again in the Library, or delete the recording."),
            cancellationToken);
        LogInterrupted(manifest.Id);
        return true;
    }

    private async Task<MediaProbe> ProbeAsync(string path, string name, CancellationToken cancellationToken)
    {
        MediaProbe probe;
        try
        {
            probe = await decoder.ProbeAsync(path, cancellationToken).WaitAsync(ProbeTimeout, cancellationToken);
        }
        catch (InvalidDataException ex)
        {
            throw Unsupported(name, ex.Message);
        }
        catch (TimeoutException)
        {
            throw Unsupported(name, string.Create(CultureInfo.InvariantCulture, $"Windows did not finish reading it within {ProbeTimeout.TotalSeconds:0} s"));
        }

        if (probe.SampleRate <= 0 || probe.Channels <= 0)
        {
            throw Unsupported(name, "it has no audio stream");
        }

        // A small hostile file can declare a format that decodes to hundreds of gigabytes of 24-bit PCM and fills the
        // drive recordings are written to; refuse what no meeting needs, and what the drive cannot hold.
        if (probe.Channels > MaxChannels || probe.SampleRate > MaxSampleRate || probe.DurationMs <= 0 || probe.DurationMs > MaxDurationMs)
        {
            throw Unsupported(
                name,
                string.Create(CultureInfo.InvariantCulture, $"it declares {probe.Channels} channels at {probe.SampleRate} Hz for {HumanFormat.Clock(Math.Max(0, probe.DurationMs))}; Memento imports up to {MaxChannels} channels at {MaxSampleRate / 1000} kHz and {MaxDurationMs / 3_600_000} hours"));
        }

        var needed = DecodedBytes(probe);
        if (freeSpace.GetFreeBytes(store.ProjectsRoot) is { } free && free - needed < ImportReserveBytes)
        {
            throw new BridgeException(
                DomainErrorCodes.LibraryImportUnsupported,
                $"\"{name}\" needs about {HumanFormat.Bytes(needed)} while it is imported, and the library drive has {HumanFormat.Bytes(free)} free; Memento keeps {HumanFormat.Bytes(ImportReserveBytes)} free for recordings. Nothing was added to the library. Free some space and import it again.",
                name);
        }

        return probe;
    }

    /// <summary>The decoded WAV (24-bit) plus the lossless track and mix written at finalize, with a margin.</summary>
    internal static long DecodedBytes(MediaProbe probe) =>
        (long)Math.Ceiling(probe.DurationMs / 1000.0 * probe.SampleRate * probe.Channels * 3 * 2.2);

    private async Task StartAsync(string id, string path, string name, MediaProbe probe, DateTimeOffset modified, CancellationToken cancellationToken)
    {
        var manifest = await catalog.UpdateAsync(
            id,
            m => m with
            {
                State = ProjectStates.Finalizing,
                DurationMs = probe.DurationMs,
                ImportedFrom = new ProjectImportSource { Path = path, Name = name },
                Tracks =
                [
                    new ProjectTrack
                    {
                        Id = TrackId,
                        SourceId = SourceId,
                        SourceKind = SourceKind,
                        Name = name,
                        File = ProjectLayout.TracksFolder + "/" + TrackId + ".wav",
                        CaptureFile = ProjectLayout.TracksFolder + "/" + TrackId + ".wav",
                        SampleRate = probe.SampleRate,
                        Channels = probe.Channels,
                        BitsPerSample = 24,
                        DurationMs = probe.DurationMs,
                    },
                ],
                Stages = [new StageStatus(StageNames.Stored, StageStates.Active, 0, "Importing")],
                Failures = m.Failures.Where(f => f.Stage != StageNames.Stored).ToList(),
            },
            cancellationToken);
        publisher.PublishProcessingProgress(new ProcessingProgressPayload(id, manifest.Stages));

        var work = Task.Run(() => RunAsync(id, path, name, probe, modified), CancellationToken.None);
        _running[id] = work;
        _ = work.ContinueWith(_ => _running.TryRemove(id, out Task? _), TaskScheduler.Default);
        LogStarted(id, probe.SampleRate, probe.Channels, probe.DurationMs, probe.HasVideo);
    }

    /// <summary>
    /// What an unfinished import made from its file: the decoded track, and the mix and peaks if storing had begun
    /// (stored files are read-only). Never the original file, which is outside the library.
    /// </summary>
    private void DeleteDecodedTracks(string recordingId)
    {
        var folder = store.GetProjectFolder(recordingId);
        var tracks = Path.Combine(folder, ProjectLayout.TracksFolder);
        var made = (Directory.Exists(tracks) ? Directory.EnumerateFiles(tracks, TrackId + ".*") : [])
            .Concat(Directory.Exists(folder) ? Directory.EnumerateFiles(folder, ProjectLayout.MixBaseName + ".*") : [])
            .Append(Path.Combine(folder, ProjectLayout.PeaksFile))
            .Where(File.Exists)
            .ToList();
        foreach (var file in made)
        {
            try
            {
                File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LogNotRemoved(ex, recordingId);
            }
        }
    }

    /// <summary>Completes when every import started so far has finished (tests, shutdown).</summary>
    public Task WhenIdleAsync() => Task.WhenAll(_running.Values);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        await _closing.CancelAsync();
        await WhenIdleAsync().WaitAsync(ShutdownWait).ContinueWith(_ => { }, TaskScheduler.Default);
        _closing.Dispose();
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    /// <summary>The file name without its extension, as a title.</summary>
    internal static string FileTitle(string name)
    {
        var stem = Path.GetFileNameWithoutExtension(name).Replace('_', ' ').Trim();
        if (stem.Length == 0)
        {
            stem = "Imported recording";
        }

        return stem.Length > ProjectService.MaxTitleLength ? stem[..ProjectService.MaxTitleLength].TrimEnd() : stem;
    }

    private static BridgeException Unsupported(string name, string reason) =>
        new(
            DomainErrorCodes.LibraryImportUnsupported,
            $"\"{name}\" can't be imported: Windows cannot read its audio ({reason.TrimEnd('.')}). Nothing was added to the library. Convert it to WAV, MP3 or M4A and import that.",
            name);

    private async Task RunAsync(string recordingId, string path, string name, MediaProbe probe, DateTimeOffset modified)
    {
        var token = _closing.Token;
        using var busy = activity.Begin(LibraryActivity.Import);
        var folder = store.GetProjectFolder(recordingId);
        try
        {
            await AppendAsync(recordingId, new HistoryEntry(time.GetLocalNow(), "recorded", "started", "Importing " + name, null), token);
            var sha256 = await FileHashes.Sha256Async(path, token);
            var progress = new DecodeProgress(recordingId, publisher);
            var decoded = await decoder.DecodeToWavAsync(path, Path.Combine(folder, ProjectLayout.TracksFolder), TrackId, progress, token);
            await catalog.UpdateAsync(
                recordingId,
                m => m with
                {
                    DurationMs = decoded.DurationMs,
                    Tracks = m.Tracks.Select(t => t with { SampleRate = decoded.SampleRate, Channels = decoded.Channels, DurationMs = decoded.DurationMs }).ToList(),
                },
                token);

            await AppendAsync(
                recordingId,
                new HistoryEntry(
                    time.GetLocalNow(),
                    "recorded",
                    "completed",
                    $"Imported {name} · {HumanFormat.Clock(decoded.DurationMs)}",
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"From {path} (SHA-256 {sha256}); the original file was not copied or changed. Decoded to 24-bit WAV, {decoded.SampleRate / 1000.0:0.#} kHz, {decoded.Channels} ch.")),
                token);
            await AppendAsync(
                recordingId,
                new HistoryEntry(
                    time.GetLocalNow(),
                    "edited",
                    "info",
                    "Date taken from the file",
                    $"The recording's date is when {name} was last modified ({modified:yyyy-MM-dd HH:mm}); change the title or details if it was recorded at another time."),
                token);
            if (probe.HasVideo || VideoExtensions.Contains(Path.GetExtension(name)))
            {
                await AppendAsync(
                    recordingId,
                    new HistoryEntry(
                        time.GetLocalNow(),
                        "recorded",
                        "info",
                        "Only the audio was imported",
                        $"{name} is a video file. This version imports its audio track only; the video stays in the original file."),
                    token);
            }

            var stored = await finalization.FinalizeAsync(recordingId, ProjectStates.Ready, token);
            if (stored.State == ProjectStates.Ready)
            {
                await processing.EnqueueAfterStoredAsync(recordingId, token);
            }

            LogImported(recordingId, decoded.DurationMs);
        }
#pragma warning disable CA1031 // A decoder failing in any way (a hostile file can make Media Foundation throw anything) must not leave a half-imported project.
        catch (Exception ex) when (ex is not OutOfMemoryException)
#pragma warning restore CA1031
        {
            // The project only holds what this import made from a file outside the library: remove it whole, so no
            // half-imported recording is left behind. The original file is untouched.
            LogFailed(ex, recordingId);
            await RemoveAsync(recordingId);
        }
    }

    private async Task RemoveAsync(string recordingId)
    {
        try
        {
            await store.DeleteAsync(recordingId, CancellationToken.None);
            await index.RemoveAsync(recordingId, CancellationToken.None);
            catalog.NotifyChanged(recordingId);
            publisher.PublishProcessingProgress(new ProcessingProgressPayload(recordingId, [new StageStatus(StageNames.Stored, StageStates.Failed, null, "Import failed")]));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ProjectNotFoundException or ProjectBusyException)
        {
            LogNotRemoved(ex, recordingId);
            try
            {
                await catalog.UpdateAsync(
                    recordingId,
                    m => m with { State = ProjectStates.Failed, Stages = [new StageStatus(StageNames.Stored, StageStates.Failed, null, "Import failed")] },
                    CancellationToken.None);
            }
            catch (Exception inner) when (inner is IOException or UnauthorizedAccessException or ProjectNotFoundException)
            {
                LogNotRemoved(inner, recordingId);
            }
        }
    }

    private async Task AppendAsync(string recordingId, HistoryEntry entry, CancellationToken cancellationToken)
    {
        try
        {
            await store.AppendHistoryAsync(recordingId, entry, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogHistoryFailed(ex, recordingId);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Import {RecordingId} started: {SampleRate} Hz, {Channels} ch, {DurationMs} ms, video: {HasVideo}")]
    private partial void LogStarted(string recordingId, int sampleRate, int channels, long durationMs, bool hasVideo);

    [LoggerMessage(Level = LogLevel.Information, Message = "Import {RecordingId} decoded and stored ({DurationMs} ms)")]
    private partial void LogImported(string recordingId, long durationMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Import {RecordingId} failed; the new project is removed and the original file is untouched")]
    private partial void LogFailed(Exception exception, string recordingId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Import {RecordingId} was cut short when Memento stopped; marked failed with Import again")]
    private partial void LogInterrupted(string recordingId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Import {RecordingId}: the unfinished project could not be removed")]
    private partial void LogNotRemoved(Exception exception, string recordingId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A history line for recording {RecordingId} could not be written")]
    private partial void LogHistoryFailed(Exception exception, string recordingId);

    /// <summary>Decoding is the first half of the <c>stored</c> stage; at most one event per percent.</summary>
    private sealed class DecodeProgress(string recordingId, BridgeEventPublisher publisher) : IProgress<double>
    {
        private int _last = -1;

        public void Report(double value)
        {
            var percent = (int)Math.Clamp(value * 100, 0, 100);
            if (Interlocked.Exchange(ref _last, percent) == percent)
            {
                return;
            }

            var label = string.Create(CultureInfo.InvariantCulture, $"{percent}% · importing");
            publisher.PublishProcessingProgress(new ProcessingProgressPayload(recordingId, [new StageStatus(StageNames.Stored, StageStates.Active, percent, label)]));
        }
    }
}
