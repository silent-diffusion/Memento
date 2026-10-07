using System.Globalization;
using Memento.Core.Audio;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Formatting;
using Memento.Core.Host;
using Memento.Core.Library;
using Memento.Core.Projects;
using Microsoft.Extensions.Logging;

namespace Memento.Core.Attachments;

/// <summary>
/// Files kept with a recording (BRIDGE.md M3 <c>attachments.*</c>): copied byte for byte into the project's
/// <c>attachments/</c> folder, indexed in <c>project.json</c> with size and SHA-256, logged in History, and counted in
/// the library's size. A name already used gets " (2)"; nothing is ever overwritten.
/// </summary>
public sealed partial class AttachmentService(
    IProjectStore store,
    ProjectCatalog catalog,
    IFilePicker picker,
    IExternalLauncher launcher,
    TimeProvider time,
    ILogger<AttachmentService> logger)
{
    /// <summary>100 MB per file (BRIDGE.md M3).</summary>
    public const long MaxBytes = 100L * 1024 * 1024;

    /// <summary>Types Windows would run rather than open; <c>attachments.open</c> shows them in their folder instead.</summary>
    private static readonly HashSet<string> RunnableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".com", ".bat", ".cmd", ".msi", ".msp", ".ps1", ".psm1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh",
        ".scr", ".pif", ".lnk", ".url", ".reg", ".hta", ".cpl", ".msc", ".jar", ".appref-ms", ".application", ".gadget",
    };

    private readonly ILogger<AttachmentService> _logger = logger;

    public static IReadOnlyList<FileFilter> PickerFilters { get; } = [new("All files", ["*.*"])];

    public async Task<IReadOnlyList<Attachment>> ListAsync(string recordingId, CancellationToken cancellationToken)
    {
        var manifest = await LoadAsync(recordingId, cancellationToken);
        return ManifestAttachments.Read(manifest).Select(a => a.ToContract()).ToList();
    }

    /// <summary><c>attachments.add</c>: the picker when <paramref name="path"/> is <c>null</c>.</summary>
    public async Task<AttachmentAddResult> AddAsync(string recordingId, string? path, CancellationToken cancellationToken)
    {
        await LoadAsync(recordingId, cancellationToken);
        var source = path;
        if (source is null)
        {
            source = await picker.PickFileAsync("Add an attachment", PickerFilters, cancellationToken);
            if (source is null)
            {
                return new AttachmentAddResult(null, Cancelled: true);
            }
        }

        M3Errors.RequireFile(source, "attachment");
        var record = await AddFileAsync(recordingId, source, Path.GetFileName(source), AttachmentRecord.FileKind, cancellationToken);
        return new AttachmentAddResult(record.ToContract(), Cancelled: false);
    }

    /// <summary>
    /// Copies <paramref name="sourcePath"/> into the project's <c>attachments/</c> folder as <paramref name="name"/>
    /// and indexes it. The copy is written to a <c>.tmp</c> file first and hashed before it is moved into place.
    /// </summary>
    /// <exception cref="BridgeException"><c>attachments.tooLarge</c>, <c>project.notFound</c>, or the copy failed.</exception>
    public async Task<AttachmentRecord> AddFileAsync(string recordingId, string sourcePath, string name, string kind, CancellationToken cancellationToken)
    {
        var manifest = await LoadAsync(recordingId, cancellationToken);
        var size = new FileInfo(sourcePath).Length;
        var displayName = FileNames.SanitizeKeepingExtension(name, "Attachment");
        if (size > MaxBytes)
        {
            throw new BridgeException(
                DomainErrorCodes.AttachmentsTooLarge,
                $"\"{displayName}\" is {HumanFormat.Bytes(size)}; attachments can be at most {HumanFormat.Bytes(MaxBytes)}. Nothing was added. Attach a smaller copy, or keep the file elsewhere and note where in the recording's notes.",
                displayName);
        }

        var folder = Path.Combine(store.GetProjectFolder(recordingId), ProjectLayout.AttachmentsFolder);
        Directory.CreateDirectory(folder);
        var taken = ManifestAttachments.Read(manifest).Select(a => Path.GetFileName(a.File)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var fileName = FileNames.Unique(folder, displayName, taken);
        var destination = Path.Combine(folder, fileName);
        var temporary = destination + ".tmp";
        string sha256;
        try
        {
            await using (var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
            {
                await input.CopyToAsync(output, cancellationToken);
                await output.FlushAsync(cancellationToken);
                output.Flush(flushToDisk: true);
            }

            sha256 = await FileHashes.Sha256Async(temporary, cancellationToken);
            File.Move(temporary, destination, overwrite: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            TryDelete(temporary);
            if (ex is OperationCanceledException)
            {
                throw;
            }

            LogCopyFailed(ex, recordingId);
            throw new BridgeException(
                BridgeErrorCodes.Internal,
                $"\"{displayName}\" could not be copied into the recording: {M3Errors.Reason(ex)}. Nothing was added and the original file is unchanged.",
                displayName);
        }

        var record = new AttachmentRecord
        {
            Id = AnnotationIds.New('f'),
            Name = fileName,
            File = ProjectLayout.AttachmentsFolder + "/" + fileName,
            SizeBytes = size,
            Sha256 = sha256,
            AddedAt = time.GetLocalNow(),
            Kind = kind,
            ContentType = ContentTypes.For(fileName),
        };

        try
        {
            await catalog.UpdateAsync(recordingId, m => ManifestAttachments.With(m, ManifestAttachments.Read(m).Append(record)), cancellationToken);
        }
        catch
        {
            // The manifest never pointed at the copy; remove it so the folder and the index agree.
            TryDelete(destination);
            throw;
        }

        await AppendHistoryAsync(
            recordingId,
            new HistoryEntry(
                record.AddedAt,
                "edited",
                "info",
                kind == AttachmentRecord.AgendaKind ? "Agenda file attached" : "Attachment added",
                string.Create(CultureInfo.InvariantCulture, $"{fileName} · {HumanFormat.Bytes(size)} · SHA-256 {sha256}")),
            cancellationToken);
        return record;
    }

    /// <summary><c>attachments.remove</c>: deletes the copy inside the recording (the UI has asked for confirmation).</summary>
    public async Task RemoveAsync(string recordingId, string attachmentId, CancellationToken cancellationToken)
    {
        var manifest = await LoadAsync(recordingId, cancellationToken);
        var record = Find(manifest, attachmentId);
        var path = FullPath(recordingId, record);
        try
        {
            if (File.Exists(path))
            {
                File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new BridgeException(
                BridgeErrorCodes.Internal,
                $"\"{record.Name}\" could not be removed: {M3Errors.Reason(ex)}. It is still attached. Close any app that has it open and try again.",
                record.Id);
        }

        await catalog.UpdateAsync(recordingId, m => ManifestAttachments.With(m, ManifestAttachments.Read(m).Where(a => a.Id != attachmentId)), cancellationToken);
        await AppendHistoryAsync(
            recordingId,
            new HistoryEntry(time.GetLocalNow(), "edited", "info", "Attachment removed", string.Create(CultureInfo.InvariantCulture, $"{record.Name} · {HumanFormat.Bytes(record.SizeBytes)}")),
            cancellationToken);
    }

    /// <summary>
    /// <c>attachments.open</c>: opens the copy with its Windows default app. A program or script is never run: its
    /// folder is opened instead, so the user decides.
    /// </summary>
    public async Task OpenAsync(string recordingId, string attachmentId, CancellationToken cancellationToken)
    {
        var manifest = await LoadAsync(recordingId, cancellationToken);
        var record = Find(manifest, attachmentId);
        var path = FullPath(recordingId, record);
        if (!File.Exists(path))
        {
            throw new BridgeException(
                DomainErrorCodes.AttachmentsNotFound,
                $"\"{record.Name}\" is listed but its file is missing from the recording's attachments folder. Nothing was changed. Remove it from the list and add the file again.",
                record.Id);
        }

        var target = RunnableExtensions.Contains(Path.GetExtension(path)) ? Path.GetDirectoryName(path)! : path;
        if (!launcher.TryOpen(new Uri(target)))
        {
            throw new BridgeException(
                BridgeErrorCodes.Internal,
                $"Windows has no app set up to open \"{record.Name}\". The file is safe in the recording. Choose a default app for {Path.GetExtension(path)} files in Windows Settings › Apps › Default apps.",
                record.Id);
        }
    }

    /// <summary>The full path of the attachment's copy inside the project folder.</summary>
    public string FullPath(string recordingId, AttachmentRecord record)
    {
        var folder = store.GetProjectFolder(recordingId);
        var full = Path.GetFullPath(Path.Combine(folder, record.File.Replace('/', Path.DirectorySeparatorChar)));
        return full.StartsWith(Path.GetFullPath(folder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? full
            : throw new BridgeException(DomainErrorCodes.AttachmentsNotFound, "That attachment's entry is damaged. Nothing was changed.", record.Id);
    }

    private static AttachmentRecord Find(ProjectManifest manifest, string attachmentId) =>
        ManifestAttachments.Read(manifest).FirstOrDefault(a => a.Id == attachmentId)
        ?? throw new BridgeException(
            DomainErrorCodes.AttachmentsNotFound,
            "That attachment is not in this recording any more; it may have been removed. Nothing was changed. Reopen the recording to see its attachments.",
            attachmentId);

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Only a leftover .tmp; it never appears in the index.
        }
    }

    private async Task<ProjectManifest> LoadAsync(string recordingId, CancellationToken cancellationToken)
    {
        try
        {
            return await store.LoadAsync(recordingId, cancellationToken);
        }
        catch (ProjectNotFoundException)
        {
            throw ProjectService.NotFound(recordingId);
        }
    }

    private async Task AppendHistoryAsync(string recordingId, HistoryEntry entry, CancellationToken cancellationToken)
    {
        try
        {
            await store.AppendHistoryAsync(recordingId, entry, cancellationToken);
            await catalog.TouchedAsync(recordingId, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogHistoryFailed(ex, recordingId);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "An attachment could not be copied into recording {RecordingId}")]
    private partial void LogCopyFailed(Exception exception, string recordingId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A history line for recording {RecordingId} could not be written")]
    private partial void LogHistoryFailed(Exception exception, string recordingId);
}
