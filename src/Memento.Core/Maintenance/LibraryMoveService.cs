using System.Globalization;
using System.Security.Cryptography;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Export;
using Memento.Core.Formatting;
using Memento.Core.Host;
using Memento.Core.Library;
using Memento.Core.Processing;
using Memento.Core.Projects;
using Memento.Core.Recording;
using Memento.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Memento.Core.Maintenance;

/// <summary>
/// <c>library.move</c> (ARCHITECTURE.md §4): copy every file of the library to the new folder, check each copy's
/// SHA-256 against the original, switch Settings to the new folder (the index is rebuilt there), and only then
/// delete the old folder. Refused while anything records, processes, imports, exports or reclaims; a failure before
/// the switch removes the copies and leaves the library where it was.
/// </summary>
public sealed partial class LibraryMoveService(
    ISettingsStore settings,
    ILibraryLocation library,
    ILibraryIndex index,
    IProjectStore store,
    ProjectCatalog catalog,
    RecordingCoordinator recordings,
    ProcessingOrchestrator processing,
    LibraryActivity activity,
    IFreeSpaceProbe freeSpace,
    M3EventPublisher events,
    TimeProvider time,
    ILogger<LibraryMoveService> logger) : IAsyncDisposable, IDisposable
{
    private readonly ILogger<LibraryMoveService> _logger = logger;
    private Task _running = Task.CompletedTask;

    public bool IsBusy => !_running.IsCompleted;

    public string StartMove(string newPath)
    {
        var target = ValidateTarget(newPath, out var source);

        // Marked as moving before the checks: recording.start marks itself starting before it checks for a move, so
        // whichever of the two comes second sees the other (LibraryActivity's lock orders them).
        var busy = activity.Begin(LibraryActivity.Move);
        try
        {
            if (Busy() is { } why)
            {
                throw new BridgeException(
                    DomainErrorCodes.LibraryBusy,
                    $"The library can't be moved while {why}. Nothing was changed. Try again when it has finished.",
                    why);
            }

            var size = LibraryUsageService.FolderSize(source);
            if (freeSpace.GetFreeBytes(target) is { } free && free < size + ExportService.SpaceMarginBytes)
            {
                throw Refused($"The drive of {target} has {HumanFormat.Bytes(free)} free and the library needs {HumanFormat.Bytes(size)}. Nothing was changed. Free up space or choose another drive.", target);
            }

            var created = !Directory.Exists(target);
            try
            {
                Directory.CreateDirectory(target);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
            {
                throw Refused($"Memento can't create {target}: {M3Errors.Reason(ex)}. Nothing was changed. Choose another folder.", target);
            }

            var jobId = "m" + Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();
            _running = Task.Run(() => RunAsync(jobId, source, target, created, size, busy), CancellationToken.None);
            LogStarted(jobId, size);
            return jobId;
        }
        catch
        {
            busy.Dispose();
            throw;
        }
    }

    /// <summary><c>library.moveRefused</c>: the target was checked and nothing was copied.</summary>
    private static BridgeException Refused(string message, string target) => new(DomainErrorCodes.LibraryMoveRefused, message, target);

    public Task WhenIdleAsync() => _running;

    /// <summary>A move cannot be interrupted safely; closing waits a while for it (the copy is verified file by file, so an exit only loses the copy).</summary>
    public async ValueTask DisposeAsync() =>
        await _running.WaitAsync(TimeSpan.FromSeconds(30)).ContinueWith(_ => { }, TaskScheduler.Default);

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    /// <summary>The library's own files: everything except the index, which is rebuilt at the new place.</summary>
    internal static bool IsIndexFile(string name) => name.StartsWith(SqliteLibraryIndex.DatabaseFileName, StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);

    private static bool IsInside(string path, string folder) =>
        (path + Path.DirectorySeparatorChar).StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static async Task<string> CopyAndHashAsync(string source, string destination, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1 << 20];
        await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true))
        await using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
        {
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
            {
                hash.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            await output.FlushAsync(cancellationToken);
            output.Flush(flushToDisk: true);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void DeleteTree(string folder, bool keepFolder)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            var attributes = File.GetAttributes(file);
            if ((attributes & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
            }
        }

        if (keepFolder)
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(folder))
            {
                if (Directory.Exists(entry))
                {
                    Directory.Delete(entry, recursive: true);
                }
                else
                {
                    File.Delete(entry);
                }
            }
        }
        else
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private string ValidateTarget(string newPath, out string source)
    {
        if (string.IsNullOrWhiteSpace(newPath) || !Path.IsPathFullyQualified(newPath))
        {
            throw Refused("The new library location needs a full folder path such as D:\\Memento Library. Nothing was changed.", newPath);
        }

        source = Normalize(library.Root);
        var target = Normalize(newPath);
        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
        {
            throw Refused($"The library is already in {target}. Nothing was changed.", target);
        }

        if (IsInside(target, source) || IsInside(source, target))
        {
            throw Refused($"{target} is inside the library or contains it, so the library can't move there. Nothing was changed. Choose a folder elsewhere.", target);
        }

        if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
        {
            throw Refused($"{target} already has files in it. Nothing was changed. Choose an empty or new folder for the library.", target);
        }

        return target;
    }

    /// <summary>What keeps the library busy, in words, or <c>null</c>.</summary>
    private string? Busy()
    {
        if (recordings.Current is not null || activity.IsStartingRecording)
        {
            return "a recording is in progress";
        }

        if (store.ListIds().Any(id => recordings.IsBusy(id) || processing.IsBusy(id)))
        {
            return "recordings are being processed";
        }

        // This move has already marked itself; another one counts.
        return IsBusy ? "the library is already being moved" : activity.Describe(except: LibraryActivity.Move);
    }

    private async Task RunAsync(string jobId, string source, string target, bool createdTarget, long totalBytes, IDisposable busy)
    {
        using (busy)
        {
            var throttle = new ProgressThrottle(time);
            void Report(int percent, string state, string? message, bool force)
            {
                if (throttle.TryPass(force))
                {
                    events.PublishLibraryMoveProgress(new LibraryMoveProgressPayload(jobId, percent, state, message, target));
                }
            }

            Report(0, "running", "Copying the library", force: true);
            var copied = 0L;
            var files = 0;
            try
            {
                foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(source, file);
                    if (IsIndexFile(Path.GetFileName(relative)) && !relative.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var destination = Path.Combine(target, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    var sourceHash = await CopyAndHashAsync(file, destination, CancellationToken.None);
                    var copyHash = await Audio.FileHashes.Sha256Async(destination, CancellationToken.None);
                    if (!string.Equals(sourceHash, copyHash, StringComparison.Ordinal))
                    {
                        throw new IOException($"the copy of {relative} does not match the original (SHA-256 differs)");
                    }

                    File.SetAttributes(destination, File.GetAttributes(file));
                    copied += new FileInfo(file).Length;
                    files++;
                    var percent = (int)Math.Min(95, copied * 95 / Math.Max(1, totalBytes));
                    Report(percent, "running", string.Create(CultureInfo.InvariantCulture, $"Copied and checked {files} files"), force: false);
                }

                if (Busy() is { } why && why != "the library is already being moved")
                {
                    throw new IOException($"{why} started during the move, so the library stayed where it was");
                }

                var newRoot = string.Equals(target, Normalize(AppPaths.DefaultLibrary), StringComparison.OrdinalIgnoreCase) ? null : target;
                await settings.UpdateAsync(s => s with { LibraryPath = newRoot }, CancellationToken.None);
                await index.InitializeAsync(CancellationToken.None);
                catalog.NotifyChanged([.. store.ListIds()]);
                LogSwitched(jobId, files);
            }
#pragma warning disable CA1031 // Any failure before the switch removes the copies and keeps the library where it was.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogFailed(ex, jobId);
                try
                {
                    DeleteTree(target, keepFolder: !createdTarget);
                }
                catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
                {
                    LogNotRemoved(cleanup, jobId);
                }

                Report(0, "failed", $"The library was not moved: {M3Errors.Reason(ex)}. It is still in {source} and nothing in it was changed; the partial copy was removed.", force: true);
                return;
            }

            string? note = null;
            try
            {
                DeleteTree(source, keepFolder: false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LogNotRemoved(ex, jobId);
                note = $" The old folder {source} could not be removed completely ({M3Errors.Reason(ex)}); everything is safe in the new place, so you can delete it in File Explorer.";
            }

            Report(100, "done", $"The library now lives in {target}; every file was copied and checked.{note}", force: true);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Library move {JobId} started ({Bytes} bytes)")]
    private partial void LogStarted(string jobId, long bytes);

    [LoggerMessage(Level = LogLevel.Information, Message = "Library move {JobId}: {Files} files copied and verified; settings switched")]
    private partial void LogSwitched(string jobId, int files);

    [LoggerMessage(Level = LogLevel.Error, Message = "Library move {JobId} failed; the library stays where it was")]
    private partial void LogFailed(Exception exception, string jobId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Library move {JobId}: a folder could not be removed")]
    private partial void LogNotRemoved(Exception exception, string jobId);
}
