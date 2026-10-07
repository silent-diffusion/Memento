using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Memento.Core.Attachments;
using Memento.Core.Audio;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Formatting;
using Memento.Core.Host;
using Memento.Core.Library;
using Memento.Core.Maintenance;
using Memento.Core.Settings;
using Memento.Core.Status;
using Microsoft.Extensions.Logging;

namespace Memento.Core.Export;

/// <summary>
/// External export (BRIDGE.md M3 <c>export.*</c>, PRODUCT-SPEC "External Export"): copies or representations of a
/// recording written outside the library, in the background, one job at a time. Files are written into a hidden
/// work folder, hashed, then moved to a name that is never already taken; <c>manifest.json</c> lists them all. A
/// failure or cancel removes every file the job wrote. The project is only read.
/// </summary>
public sealed partial class ExportService(
    ExportPlanner planner,
    ILibraryLocation library,
    IFreeSpaceProbe freeSpace,
    ISettingsStore settings,
    IExternalLauncher launcher,
    M3EventPublisher events,
    ExportStatusBoard board,
    FooterStatusService footer,
    LibraryActivity activity,
    IAppInfo app,
    TimeProvider time,
    ILogger<ExportService> logger) : IAsyncDisposable, IDisposable
{
    /// <summary>Room left on the drive beyond the estimate before an export is refused.</summary>
    public const long SpaceMarginBytes = 16L * 1024 * 1024;

    private const int KeptJobs = 20;
    private const string WorkFolderPrefix = ".memento-export-";

    private readonly ConcurrentDictionary<string, ExportJob> _jobs = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Task> _running = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);
    private readonly ILogger<ExportService> _logger = logger;
    private int _disposed;

    public bool IsBusy => !_running.IsEmpty;

    public async Task<ExportEstimate> EstimateAsync(string recordingId, ExportSelection selection, CancellationToken cancellationToken)
    {
        Validate(selection);
        var plan = await planner.PlanAsync(recordingId, selection, cancellationToken);
        var items = plan.Items.Select(i => new ExportEstimateItem(i.Component, i.Folder is null ? i.Name : i.Folder + "/" + i.Name, i.EstimatedBytes) { DocumentId = i.DocumentId }).ToList();

        // manifest.json is written beside any export; it is counted in the totals, not as a row's file.
        var manifest = items.Count > 0 ? 1 : 0;
        return new ExportEstimate(items.Count + manifest, items.Sum(i => i.Bytes) + (manifest * plan.ManifestEstimate), items, plan.Unavailable);
    }

    /// <summary>
    /// Checks everything that can be checked before writing (selection, something to write, the destination and its
    /// free space), stores the defaults when asked, then starts the job and returns its id.
    /// </summary>
    public async Task<string> RunAsync(ExportRunParams parameters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        activity.ThrowIfMoving();
        Validate(parameters.Selection);
        var destination = parameters.Destination ?? throw M3Errors.Invalid("Say where to export: destination.folder is required.");
        var plan = await planner.PlanAsync(parameters.RecordingId, parameters.Selection, cancellationToken);
        if (plan.Items.Count == 0)
        {
            var reasons = plan.Unavailable.Count > 0
                ? string.Join("; ", plan.Unavailable.Select(u => $"{ExportComponents.Label(u.Component)}: {u.Reason.ToLowerInvariant()}"))
                : "every row is unticked";
            throw new BridgeException(
                DomainErrorCodes.ExportNothingSelected,
                $"There is nothing to export: {reasons}. Nothing was written. Tick at least one row that is available.");
        }

        var folder = CheckDestination(destination.Folder, plan.EstimatedBytes);
        if (parameters.Remember)
        {
            await settings.UpdateAsync(
                s => s with { Export = s.Export with { Defaults = parameters.Selection, DefaultFolder = folder, CreateSubfolder = destination.CreateSubfolder } },
                cancellationToken);
        }

        var id = "x" + Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();
        var job = new ExportJob(id, plan, destination with { Folder = folder });
        _jobs[id] = job;
        Trim();
        var work = Task.Run(() => RunJobAsync(job), CancellationToken.None);
        _running[id] = work;
        _ = work.ContinueWith(_ => _running.TryRemove(id, out Task? _), TaskScheduler.Default);
        LogQueued(id, plan.RecordingId, plan.Items.Count, plan.EstimatedBytes);
        return id;
    }

    /// <summary><c>export.cancel</c>: a finished job is left as it is.</summary>
    public void Cancel(string jobId)
    {
        var job = Find(jobId);
        if (!job.IsFinished)
        {
            job.Cancel.Cancel();
        }
    }

    /// <summary><c>export.openFolder</c>: the folder the files went to, or the chosen destination when nothing is left there.</summary>
    public void OpenFolder(string jobId)
    {
        var job = Find(jobId);
        var folder = job.OutputFolder is { } output && Directory.Exists(output) ? output : job.Destination.Folder;
        if (!Directory.Exists(folder) || !launcher.TryOpen(new Uri(folder)))
        {
            throw new BridgeException(
                BridgeErrorCodes.Internal,
                $"Windows could not open {folder} in File Explorer. The exported files are not affected; open the folder from File Explorer.",
                folder);
        }
    }

    /// <summary>Completes when every export started so far has finished (tests, shutdown).</summary>
    public Task WhenIdleAsync() => Task.WhenAll(_running.Values);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        foreach (var job in _jobs.Values)
        {
            if (!job.IsFinished)
            {
                await job.Cancel.CancelAsync();
            }
        }

        // A cancelled export removes its files; give it a moment to do so.
        await WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(10)).ContinueWith(_ => { }, TaskScheduler.Default);
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    private static void Validate(ExportSelection? selection)
    {
        if (ExportRules.Validate(selection) is { } problem)
        {
            throw M3Errors.Invalid(problem);
        }
    }

    private static BridgeException Unwritable(string folder, string why) =>
        new(DomainErrorCodes.ExportDestinationUnwritable, $"Memento can't write to {folder}: {why}. Nothing was written and nothing inside Memento was changed. Choose another folder.", why);

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: the caller reports the failure that led here.
        }
    }

    private ExportJob Find(string jobId) =>
        _jobs.TryGetValue(jobId ?? string.Empty, out var job)
            ? job
            : throw new BridgeException(DomainErrorCodes.ExportNotFound, "That export is not known to this session of Memento; it may have run before Memento was restarted. Nothing was changed.", jobId);

    /// <summary>Returns the full folder path, or throws <c>export.destinationUnwritable</c> with the reason.</summary>
    private string CheckDestination(string? folder, long estimatedBytes)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathFullyQualified(folder))
        {
            throw Unwritable(folder ?? string.Empty, "it is not a full folder path such as D:\\Exports");
        }

        var full = Path.GetFullPath(folder);
        var libraryRoot = Path.GetFullPath(library.Root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if ((full.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar).StartsWith(libraryRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw Unwritable(full, "it is inside the Memento library, and exports are copies kept outside it");
        }

        try
        {
            Directory.CreateDirectory(full);
            var probe = Path.Combine(full, WorkFolderPrefix + "check-" + Guid.NewGuid().ToString("N") + ".tmp");
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
            {
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            throw Unwritable(full, M3Errors.Reason(ex));
        }

        if (freeSpace.GetFreeBytes(full) is { } free && free < estimatedBytes + SpaceMarginBytes)
        {
            throw Unwritable(full, $"the drive has {HumanFormat.Bytes(free)} free and this export needs about {HumanFormat.Bytes(estimatedBytes)}");
        }

        return full;
    }

    private void Trim()
    {
        foreach (var old in _jobs.Values.Where(j => j.IsFinished).OrderBy(j => j.Order).SkipLast(KeptJobs).ToList())
        {
            if (_jobs.TryRemove(old.Id, out var removed))
            {
                removed.Dispose();
            }
        }
    }

    private void Publish(ExportJob job, bool force, ProgressThrottle throttle)
    {
        if (!throttle.TryPass(force))
        {
            return;
        }

        events.PublishExportProgress(job.ToPayload());
        board.Set(job.IsFinished ? ExportFooterStatus.Idle : new ExportFooterStatus(true, job.Percent, job.Plan.Title));
        footer.Publish(force: false);
    }

    private async Task RunJobAsync(ExportJob job)
    {
        var throttle = new ProgressThrottle(time);
        await _oneAtATime.WaitAsync();
        try
        {
            using var busy = activity.Begin(LibraryActivity.Export);
            await WriteAsync(job, throttle);
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    private async Task WriteAsync(ExportJob job, ProgressThrottle throttle)
    {
        var token = job.Cancel.Token;
        var plan = job.Plan;
        var written = new List<string>();
        var createdFolders = new List<string>();
        string? work = null;
        try
        {
            token.ThrowIfCancellationRequested();
            var output = job.Destination.Folder;
            if (job.Destination.CreateSubfolder)
            {
                output = Path.Combine(output, FileNames.Unique(output, plan.BaseName));
                Directory.CreateDirectory(output);
                createdFolders.Add(output);
            }

            job.OutputFolder = output;
            Publish(job, force: true, throttle);
            work = Path.Combine(output, WorkFolderPrefix + job.Id);
            var workInfo = Directory.CreateDirectory(work);
            workInfo.Attributes |= FileAttributes.Hidden;

            var files = new List<ExportManifestFile>();
            var taken = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var total = Math.Max(1, plan.EstimatedBytes);
            long doneEstimate = 0;
            foreach (var item in plan.Items)
            {
                token.ThrowIfCancellationRequested();
                var folder = output;
                if (item.Folder is not null)
                {
                    folder = Path.Combine(output, item.Folder);
                    if (!Directory.Exists(folder))
                    {
                        Directory.CreateDirectory(folder);
                        createdFolders.Add(folder);
                    }
                }

                if (!taken.TryGetValue(folder, out var names))
                {
                    names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    taken[folder] = names;
                }

                var name = FileNames.Unique(folder, item.Name, names);
                job.CurrentFile = name;
                Publish(job, force: false, throttle);
                var bytes = await WriteFileAsync(item.WriteAsync, Path.Combine(folder, name), work, name, written, token);
                var relative = item.Folder is null ? name : item.Folder + "/" + name;
                files.Add(new ExportManifestFile(relative, bytes.Length, bytes.Sha256));
                job.Files++;
                job.Bytes += bytes.Length;
                doneEstimate += item.EstimatedBytes;
                job.Percent = (int)Math.Min(99, doneEstimate * 100 / total);
                Publish(job, force: false, throttle);
            }

            token.ThrowIfCancellationRequested();
            var manifest = new ExportManifestDocument
            {
                MementoVersion = app.Version,
                RecordingId = plan.RecordingId,
                Title = plan.Title,
                ExportedAt = plan.ExportedAt,
                Files = files,
            };
            var manifestName = FileNames.Unique(output, ExportNaming.ManifestFile, taken.GetValueOrDefault(output));
            job.CurrentFile = manifestName;
            var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, ExportJsonContext.Default.ExportManifestDocument);
            var written2 = await WriteFileAsync((path, ct) => File.WriteAllBytesAsync(path, manifestBytes, ct), Path.Combine(output, manifestName), work, manifestName, written, token);
            job.Files++;
            job.Bytes += written2.Length;
            RemoveWorkFolder(work);
            job.CurrentFile = null;
            job.Percent = 100;
            job.State = ExportJob.Done;
            LogDone(job.Id, plan.RecordingId, job.Files, job.Bytes);
        }
        catch (OperationCanceledException)
        {
            Clean(written, work, createdFolders);
            job.State = ExportJob.Cancelled;
            job.Message = "The export was cancelled. The files it had written were removed; nothing inside Memento was changed.";
            job.Files = 0;
            job.Bytes = 0;
            LogCancelled(job.Id);
        }
#pragma warning disable CA1031 // Any failure is reported in the job's message; the files written so far are removed.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Clean(written, work, createdFolders);
            var file = job.CurrentFile is { } current ? $"\"{current}\"" : "The export folder";
            job.State = ExportJob.Failed;
            job.Message = $"{file} could not be written: {M3Errors.Reason(ex)}. Nothing inside Memento was changed, and the files this export had written were removed. Try again, or choose another folder.";
            job.Files = 0;
            job.Bytes = 0;
            LogFailed(ex, job.Id, plan.RecordingId);
        }

        Publish(job, force: true, throttle);
    }

    /// <summary>Writes one file into the work folder, hashes it, and moves it to <paramref name="destination"/> (never over an existing file).</summary>
    private static async Task<(long Length, string Sha256)> WriteFileAsync(
        Func<string, CancellationToken, Task> write,
        string destination,
        string work,
        string name,
        List<string> written,
        CancellationToken cancellationToken)
    {
        var temporary = Path.Combine(work, Guid.NewGuid().ToString("N") + Path.GetExtension(name));
        try
        {
            await write(temporary, cancellationToken);
            var sha256 = await FileHashes.Sha256Async(temporary, cancellationToken);
            var length = new FileInfo(temporary).Length;
            File.Move(temporary, destination, overwrite: false);
            written.Add(destination);
            return (length, sha256);
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    private void Clean(List<string> written, string? work, List<string> createdFolders)
    {
        foreach (var path in written)
        {
            TryDelete(path);
        }

        if (work is not null)
        {
            RemoveWorkFolder(work);
        }

        foreach (var folder in Enumerable.Reverse(createdFolders))
        {
            try
            {
                if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
                {
                    Directory.Delete(folder);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LogNotCleaned(ex, folder);
            }
        }
    }

    private void RemoveWorkFolder(string work)
    {
        try
        {
            if (Directory.Exists(work))
            {
                Directory.Delete(work, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogNotCleaned(ex, work);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Export {JobId} of recording {RecordingId} queued: {Files} files, about {Bytes} bytes")]
    private partial void LogQueued(string jobId, string recordingId, int files, long bytes);

    [LoggerMessage(Level = LogLevel.Information, Message = "Export {JobId} of recording {RecordingId} done: {Files} files, {Bytes} bytes")]
    private partial void LogDone(string jobId, string recordingId, int files, long bytes);

    [LoggerMessage(Level = LogLevel.Information, Message = "Export {JobId} cancelled; its files were removed")]
    private partial void LogCancelled(string jobId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Export {JobId} of recording {RecordingId} failed; its files were removed")]
    private partial void LogFailed(Exception exception, string jobId, string recordingId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Export clean-up could not remove {Path}")]
    private partial void LogNotCleaned(Exception exception, string path);
}
