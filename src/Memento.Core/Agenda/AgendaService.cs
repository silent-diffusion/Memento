using System.Globalization;
using Memento.Core.Attachments;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Host;
using Memento.Core.Library;
using Memento.Core.Projects;
using Microsoft.Extensions.Logging;

namespace Memento.Core.Agendas;

/// <summary>
/// Agenda import (BRIDGE.md M3 <c>agenda.*</c>): read a file, a drop or pasted text on this PC, show the parsed items
/// for review, then store them as the recording's agenda with the original file kept in <c>attachments/</c>.
/// </summary>
public sealed partial class AgendaService(
    IProjectStore store,
    ProjectCatalog catalog,
    ProjectService projects,
    AttachmentService attachments,
    IAgendaReader reader,
    PendingAgendaFiles pending,
    DroppedFiles dropped,
    IFilePicker picker,
    TimeProvider time,
    ILogger<AgendaService> logger)
{
    /// <summary>At most 200 items (matches the parsers and the Details sheet).</summary>
    public const int MaxItems = 200;

    /// <summary>At most 200 characters per item.</summary>
    public const int MaxItemLength = 200;

    public const string PastedTextSource = "Pasted text";

    private readonly ILogger<AgendaService> _logger = logger;

    /// <summary>The picker's file types: every format the parsers read, then all of them together.</summary>
    public static IReadOnlyList<FileFilter> PickerFilters { get; } =
    [
        new("Agendas", ["*.docx", "*.pdf", "*.xlsx", "*.csv", "*.tsv", "*.md", "*.markdown", "*.txt", "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.tif", "*.tiff", "*.heic"]),
        new("Word documents", ["*.docx"]),
        new("PDF files", ["*.pdf"]),
        new("Excel workbooks", ["*.xlsx"]),
        new("CSV and TSV files", ["*.csv", "*.tsv"]),
        new("Markdown files", ["*.md", "*.markdown"]),
        new("Text files", ["*.txt"]),
        new("Images", ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.tif", "*.tiff", "*.heic"]),
        new("All files", ["*.*"]),
    ];

    /// <summary><c>agenda.importFile</c>: the picker when <paramref name="path"/> is <c>null</c>.</summary>
    public async Task<AgendaImportResult> ImportFileAsync(string recordingId, string? path, CancellationToken cancellationToken)
    {
        RequireProject(recordingId);
        var source = path;
        if (source is null)
        {
            source = await picker.PickFileAsync("Choose an agenda", PickerFilters, cancellationToken);
            if (source is null)
            {
                return new AgendaImportResult(null, Cancelled: true);
            }
        }

        M3Errors.RequireFile(source, "agenda");
        return new AgendaImportResult(await ReadFileAsync(recordingId, source, cancellationToken), Cancelled: false);
    }

    /// <summary><c>agenda.importDropped</c>: the first dropped file the host has the real path of.</summary>
    public async Task<AgendaImportResult> ImportDroppedAsync(string recordingId, IReadOnlyList<string> names, CancellationToken cancellationToken)
    {
        RequireProject(recordingId);
        var path = dropped.Resolve(names ?? []);
        if (path is null)
        {
            throw new BridgeException(
                DomainErrorCodes.AgendaDropUnavailable,
                "Memento could not get the dropped file from Windows. Nothing was imported. Choose the file with Choose a file instead.");
        }

        return new AgendaImportResult(await ReadFileAsync(recordingId, path, cancellationToken), Cancelled: false);
    }

    /// <summary><c>agenda.parseText</c>.</summary>
    public async Task<AgendaPreviewResult> ParseTextAsync(string recordingId, string text, CancellationToken cancellationToken)
    {
        RequireProject(recordingId);
        var reading = await reader.ReadTextAsync(text ?? string.Empty, cancellationToken);
        return new AgendaPreviewResult(new AgendaParsePreview(PastedTextSource, AgendaSourceKinds.PastedText, reading.Title, reading.Items, reading.Warnings, reading.OcrEngine, null));
    }

    /// <summary>
    /// <c>agenda.apply</c>: the reviewed items replace the agenda (new ids, not covered). The held original is copied
    /// into <c>attachments/</c> as an <c>agenda</c> attachment; a token that expired only skips that copy, so the
    /// reviewed items are never lost, and History says so.
    /// </summary>
    public async Task<Project> ApplyAsync(AgendaApplyParams parameters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var items = ValidateItems(parameters.Items ?? []);
        if (!AgendaSourceKinds.IsValid(parameters.SourceKind))
        {
            throw M3Errors.Invalid($"Agenda source kind '{parameters.SourceKind}' is not one of {string.Join(", ", AgendaSourceKinds.All)}.");
        }

        var source = (parameters.Source ?? string.Empty).Trim();
        if (source.Length is 0 or > ProjectService.MaxTitleLength)
        {
            throw M3Errors.Invalid($"The agenda source needs a name of 1 to {ProjectService.MaxTitleLength} characters, such as the file name.");
        }

        RequireProject(parameters.RecordingId);
        var held = parameters.AttachmentToken is { } token ? pending.Find(token) : null;
        if (held is not null && held.RecordingId != parameters.RecordingId)
        {
            held = null;
        }

        var agenda = new Bridge.Contracts.Agenda(source, ParsedLocally: true, items);
        await catalog.UpdateAsync(parameters.RecordingId, m => m with { Details = m.Details with { Agenda = agenda } }, cancellationToken);

        string? attached = null;
        if (held is not null)
        {
            var record = await attachments.AddFileAsync(parameters.RecordingId, held.Path, held.Name, AttachmentRecord.AgendaKind, cancellationToken);
            attached = record.Name;
            pending.Release(held.Token);
        }

        var detail = string.Create(
            CultureInfo.InvariantCulture,
            $"{items.Count} {(items.Count == 1 ? "item" : "items")} from {AgendaSourceKinds.Describe(parameters.SourceKind)}, parsed on this PC");
        if (items.Any(i => i.Uncertain))
        {
            detail += string.Create(CultureInfo.InvariantCulture, $" · {items.Count(i => i.Uncertain)} marked uncertain");
        }

        if (attached is not null)
        {
            detail += $" · the original is kept in the attachments as {attached}";
        }
        else if (parameters.AttachmentToken is not null)
        {
            detail += " · the original file was not attached because it was imported more than an hour ago; import it again to keep it with the recording";
        }

        await AppendHistoryAsync(parameters.RecordingId, new HistoryEntry(time.GetLocalNow(), "edited", "info", "Agenda imported", detail), cancellationToken);
        LogApplied(parameters.RecordingId, items.Count, parameters.SourceKind);
        return await projects.GetAsync(parameters.RecordingId, cancellationToken);
    }

    /// <summary><c>agenda.discard</c>: forgets a held original; an unknown or expired token is not an error.</summary>
    public void Discard(string attachmentToken) => pending.Release(attachmentToken ?? string.Empty);

    /// <summary><c>agenda.setCovered</c>: works during recording too.</summary>
    public async Task<Bridge.Contracts.Agenda> SetCoveredAsync(string recordingId, string itemId, bool covered, CancellationToken cancellationToken)
    {
        RequireProject(recordingId);
        var saved = await catalog.UpdateAsync(
            recordingId,
            m =>
            {
                var agenda = m.Details.Agenda;
                if (!agenda.Items.Any(i => i.Id == itemId))
                {
                    throw new BridgeException(
                        DomainErrorCodes.AnnotationsNotFound,
                        "That agenda item is not in this recording any more; the agenda may have been replaced. Nothing was changed. Reopen the recording to see its agenda.",
                        itemId);
                }

                return m with
                {
                    Details = m.Details with
                    {
                        Agenda = agenda with { Items = agenda.Items.Select(i => i.Id == itemId ? i with { Covered = covered } : i).ToList() },
                    },
                };
            },
            cancellationToken);
        return saved.Details.Agenda;
    }

    private static List<AgendaItem> ValidateItems(IReadOnlyList<AgendaApplyItem> items)
    {
        var kept = new List<AgendaItem>(items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            var text = (items[i]?.Text ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                continue;
            }

            if (text.Length > MaxItemLength)
            {
                var position = (i + 1).ToString(CultureInfo.InvariantCulture);
                throw new BridgeException(
                    DomainErrorCodes.AgendaItemTooLong,
                    $"Item {position} (\"{text[..40].TrimEnd()}…\") is {text.Length.ToString(CultureInfo.InvariantCulture)} characters; agenda items can be at most {MaxItemLength}. Nothing was saved. Shorten it or split it into separate items.",
                    position);
            }

            var reason = items[i].Uncertain ? items[i].UncertainReason?.Trim() : null;
            kept.Add(new AgendaItem(AnnotationIds.New('a'), text, Covered: false, items[i].Uncertain, string.IsNullOrEmpty(reason) ? null : reason));
        }

        if (kept.Count > MaxItems)
        {
            throw new BridgeException(
                DomainErrorCodes.AgendaTooManyItems,
                string.Create(CultureInfo.InvariantCulture, $"The agenda has {kept.Count} items; a recording's agenda can have at most {MaxItems}. Nothing was saved. Remove some items or combine them."),
                kept.Count.ToString(CultureInfo.InvariantCulture));
        }

        return kept;
    }

    private async Task<AgendaParsePreview> ReadFileAsync(string recordingId, string path, CancellationToken cancellationToken)
    {
        var reading = await reader.ReadFileAsync(path, cancellationToken);
        string token;
        try
        {
            token = await pending.HoldAsync(path, recordingId, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new BridgeException(
                DomainErrorCodes.AgendaUnreadable,
                $"\"{Path.GetFileName(path)}\" was read but could not be kept for attaching: {M3Errors.Reason(ex)}. Nothing was imported. Try again, or paste the items as text.");
        }

        return new AgendaParsePreview(Path.GetFileName(path), reading.SourceKind, reading.Title, reading.Items, reading.Warnings, reading.OcrEngine, token);
    }

    private void RequireProject(string recordingId)
    {
        if (!store.Exists(recordingId))
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Agenda applied to recording {RecordingId}: {Items} items from {SourceKind}")]
    private partial void LogApplied(string recordingId, int items, string sourceKind);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A history line for recording {RecordingId} could not be written")]
    private partial void LogHistoryFailed(Exception exception, string recordingId);
}
