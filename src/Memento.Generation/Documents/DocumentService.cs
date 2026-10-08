using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Export;
using Memento.Core.Library;
using Memento.Core.Projects;
using Memento.Core.Settings;
using Memento.Documents.Export;
using Memento.Documents.Model;
using Memento.Documents.Model.Blocks;
using Memento.Documents.Model.Modules;
using Memento.Documents.Render;
using Memento.Documents.Templates;
using Memento.Generation.Bridge;
using Microsoft.Extensions.Logging;
using BridgeTemplate = Memento.Core.Bridge.Contracts.Template;

namespace Memento.Generation.Documents;

/// <summary>
/// The <c>documents.*</c> methods: list, open, render (viewer paper and print HTML), create, light edits from the viewer
/// (parsed back into blocks; markup the viewer never produces is refused), rename, copy, delete, make a template, versions
/// and restore, and single-document export. Every write goes through <see cref="ProjectDocumentStore"/> (atomic, versioned),
/// raises <c>documents.changed</c> and <c>library.changed</c>, and the meaningful ones append a History line.
/// </summary>
public sealed partial class DocumentService(
    ProjectDocumentStore store,
    IProjectStore projects,
    ProjectCatalog catalog,
    StyleService styles,
    TemplateService templates,
    ModuleCatalog modules,
    DocumentHtmlRenderer renderer,
    DocumentExporter exporter,
    ISettingsStore settings,
    M4EventPublisher events,
    TimeProvider time,
    ILogger<DocumentService> logger)
{
    public const int MaxNameLength = 120;

    private readonly ILogger<DocumentService> _logger = logger;

    public async Task<IReadOnlyList<DocumentSummary>> ListAsync(string recordingId, CancellationToken cancellationToken)
    {
        EnsureProject(recordingId);
        var list = new List<DocumentSummary>();
        foreach (var document in await store.ListAsync(recordingId, cancellationToken))
        {
            list.Add(await SummaryAsync(recordingId, document, cancellationToken));
        }

        return list;
    }

    public async Task<DocumentGetResult> GetAsync(string recordingId, string documentId, CancellationToken cancellationToken)
    {
        var document = await LoadAsync(recordingId, documentId, cancellationToken);
        return new DocumentGetResult(M4Mapping.ToContent(document), await SummaryAsync(recordingId, document, cancellationToken));
    }

    public async Task<Document> LoadAsync(string recordingId, string documentId, CancellationToken cancellationToken)
    {
        EnsureProject(recordingId);
        Document? document;
        try
        {
            document = await store.LoadAsync(recordingId, documentId, cancellationToken);
        }
        catch (DocumentFormatException ex)
        {
            throw M4Errors.Invalid(ex.Message, documentId);
        }

        return document ?? throw M4Errors.DocumentNotFound(documentId);
    }

    /// <param name="mode"><c>view</c> (the viewer's paper) or <c>print</c> (the whole page the PDF is printed from).</param>
    public async Task<HtmlResult> RenderHtmlAsync(string recordingId, string documentId, string mode, CancellationToken cancellationToken)
    {
        if (mode is not ("view" or "print"))
        {
            throw M4Errors.Invalid($"mode: '{mode}' is not available. Choose view or print.", "mode");
        }

        var document = await LoadAsync(recordingId, documentId, cancellationToken);
        var style = await styles.FindOrDefaultAsync(document.StyleId, cancellationToken);
        if (mode == "print")
        {
            return new HtmlResult(renderer.RenderPrintHtml(document, style));
        }

        var paper = renderer.RenderViewer(document, style);
        return new HtmlResult(paper.Html);
    }

    /// <summary>A hand-written document with one empty text module.</summary>
    public async Task<DocumentSummary> CreateAsync(string recordingId, string name, string? styleId, CancellationToken cancellationToken)
    {
        var manifest = await ManifestAsync(recordingId, cancellationToken);
        var title = CheckName(name);
        var style = await styles.FindAsync(styleId ?? settings.Current.Documents.DefaultStyleId ?? Memento.Documents.Styling.BuiltInStyles.CorporateId, cancellationToken);
        var id = ProjectDocumentStore.NewId();
        var saved = await store.WriteAsync(
            recordingId,
            id,
            DocumentChangeReasons.Created,
            _ => new Document
            {
                Title = title,
                Name = title,
                StyleId = style.Id,
                Meta = new DocumentMeta { RecordedAt = manifest.CreatedAt, DurationMs = manifest.DurationMs, RecordingTitle = manifest.Details.Title, RecordingId = recordingId },
                Rows =
                [
                    DocumentRow.Of(new ModuleBlock
                    {
                        Id = "t1",
                        Type = ModuleIds.CustomText,
                        Title = "Notes",
                        Provenance = Provenance.FromUser(),
                        Blocks = [new ParagraphBlock { Runs = [Run.Plain(string.Empty)] }],
                    }),
                ],
            },
            cancellationToken);
        await ChangedAsync(recordingId, id, DocumentChangeReasons.Created, $"Document \"{title}\" created", null, cancellationToken);
        return await SummaryAsync(recordingId, saved!, cancellationToken);
    }

    /// <summary>The viewer's light edits: the paper markup parsed back into blocks (HtmlToBlocks).</summary>
    public async Task<DocumentSaveEditResult> SaveEditAsync(string recordingId, string documentId, string html, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(html);
        if (Unsupported(html) is { } problem)
        {
            throw M4Errors.UnsupportedEdit(problem);
        }

        var current = await LoadAsync(recordingId, documentId, cancellationToken);
        var parsed = HtmlToBlocks.ParsePaper(html);
        if (parsed.Rows.Count == 0 && current.Rows.Count > 0)
        {
            throw M4Errors.UnsupportedEdit("the markup holds none of the document's sections");
        }

        var firstOfRun = current.LastChange?.Reason != DocumentChangeReasons.Edited;
        Document? saved;
        try
        {
            saved = await store.WriteAsync(
                recordingId,
                documentId,
                DocumentChangeReasons.Edited,
                existing =>
                {
                    var edited = HtmlToBlocks.Apply(existing ?? current, html);
                    return Same(edited, existing ?? current) ? null : edited;
                },
                cancellationToken);
        }
        catch (DocumentFormatException ex)
        {
            throw M4Errors.UnsupportedEdit(ex.Message.TrimEnd('.'));
        }

        if (saved is null)
        {
            return new DocumentSaveEditResult(M4Mapping.ToContent(current), current.Version);
        }

        await ChangedAsync(recordingId, documentId, DocumentChangeReasons.Edited, firstOfRun ? $"Document \"{saved.Name ?? saved.Title}\" edited" : null, null, cancellationToken);
        return new DocumentSaveEditResult(M4Mapping.ToContent(saved), saved.Version);
    }

    public async Task<DocumentSummary> RenameAsync(string recordingId, string documentId, string? name, CancellationToken cancellationToken)
    {
        var value = CheckName(name);
        await LoadAsync(recordingId, documentId, cancellationToken);
        var saved = await store.WriteAsync(recordingId, documentId, DocumentChangeReasons.Renamed, d => d is null ? null : d with { Name = value, Title = d.Generation is null ? value : d.Title }, cancellationToken)
            ?? throw M4Errors.DocumentNotFound(documentId);
        await ChangedAsync(recordingId, documentId, DocumentChangeReasons.Edited, $"Document renamed to \"{value}\"", null, cancellationToken);
        return await SummaryAsync(recordingId, saved, cancellationToken);
    }

    public async Task<DocumentSummary> DuplicateAsync(string recordingId, string documentId, string? name, CancellationToken cancellationToken)
    {
        var source = await LoadAsync(recordingId, documentId, cancellationToken);
        var value = name is null ? CheckName((source.Name ?? source.Title) + " (copy)") : CheckName(name);
        var id = ProjectDocumentStore.NewId();
        var saved = await store.WriteAsync(recordingId, id, DocumentChangeReasons.Created, _ => source with { Name = value, Title = source.Generation is null ? value : source.Title, CreatedAt = default }, cancellationToken);
        await ChangedAsync(recordingId, id, DocumentChangeReasons.Created, $"Document \"{value}\" created as a copy", null, cancellationToken);
        return await SummaryAsync(recordingId, saved!, cancellationToken);
    }

    public async Task DeleteAsync(string recordingId, string documentId, CancellationToken cancellationToken)
    {
        var document = await LoadAsync(recordingId, documentId, cancellationToken);
        if (!await store.DeleteAsync(recordingId, documentId, cancellationToken))
        {
            throw M4Errors.DocumentNotFound(documentId);
        }

        await ChangedAsync(recordingId, documentId, DocumentChangeReasons.Deleted, $"Document \"{document.Name ?? document.Title}\" deleted", null, cancellationToken);
    }

    /// <summary>A new template from the document: its generation's template with the document's layout and module headings.</summary>
    public async Task<BridgeTemplate> MakeTemplateAsync(string recordingId, string documentId, string? name, CancellationToken cancellationToken)
    {
        var document = await LoadAsync(recordingId, documentId, cancellationToken);
        var value = CheckName(name ?? (document.Name ?? document.Title));
        DocumentTemplate? origin = null;
        if (document.Record?.TemplateId is { Length: > 0 } templateId)
        {
            try
            {
                origin = await templates.FindAsync(templateId, cancellationToken);
            }
            catch (BridgeException)
            {
                origin = null;
            }
        }

        var byId = origin?.Modules().ToDictionary(m => m.Id, StringComparer.Ordinal) ?? [];
        var rows = document.Rows.Select(r => new TemplateRow
        {
            Modules = r.Modules.Where(m => modules.Find(m.Type) is not null).Select(m => (byId.TryGetValue(m.Id, out var t) ? t : TemplateModule.FromCatalog(modules.Get(m.Type), m.Id)) with
            {
                Title = m.Title,
                TextSize = m.TextSize,
                LinkToTranscript = m.LinkToTranscript,
            }).ToList(),
        }).Where(r => r.Modules.Count > 0).ToList();
        var inputs = document.Record?.Inputs;
        var template = (origin ?? new DocumentTemplate { DocumentKind = document.Meta.Kind ?? value }) with
        {
            Id = string.Empty,
            Name = value,
            BuiltIn = false,
            Rows = rows,
            DefaultStyleId = document.StyleId ?? origin?.DefaultStyleId ?? Memento.Documents.Styling.BuiltInStyles.CorporateId,
            ProviderId = document.Generation?.ProviderId ?? origin?.ProviderId,
            Inputs = inputs is null ? origin?.Inputs ?? new TemplateInputs() : (origin?.Inputs ?? new TemplateInputs()) with
            {
                Transcript = inputs.Transcript,
                Details = inputs.Details,
                Participants = inputs.Participants,
                Agenda = inputs.Agenda,
                Highlights = inputs.Highlights,
                ImportedDocuments = inputs.Attachments,
                PreviousDocuments = inputs.PreviousDocuments,
            },
        };
        return await templates.SaveAsync(M4Mapping.ToBridge(template) with { Id = string.Empty }, cancellationToken);
    }

    public async Task<IReadOnlyList<DocumentVersionInfo>> VersionsAsync(string recordingId, string documentId, CancellationToken cancellationToken)
    {
        var current = await LoadAsync(recordingId, documentId, cancellationToken);
        if (!settings.Current.History.KeepVersions)
        {
            return [];
        }

        // The current content first (it is not a kept copy and cannot be restored), then the kept versions.
        var versions = await store.ListVersionsAsync(recordingId, documentId, cancellationToken);
        return [Current(current), .. versions.Select(v => new DocumentVersionInfo(v.Id, v.SavedAt, v.Reason, Changes(v.Document, current), v.Document.Version))];
    }

    /// <summary>The id of the current content in <c>documents.versions</c>.</summary>
    public const string CurrentVersionId = "current";

    private static DocumentVersionInfo Current(Document document)
    {
        var reason = document.LastChange?.Reason switch
        {
            DocumentChangeReasons.Generated or DocumentChangeReasons.Regenerated or DocumentChangeReasons.Restored or DocumentChangeReasons.Edited => document.LastChange.Reason,
            _ => document.Generation is null ? DocumentChangeReasons.Edited : DocumentChangeReasons.Generated,
        };
        return new DocumentVersionInfo(CurrentVersionId, document.LastChange?.At ?? document.ModifiedAt, reason, 0, document.Version);
    }

    /// <summary><c>documents.getVersion</c>: a kept version's content and its paper, drawn with the style it names.</summary>
    public async Task<DocumentVersionResult> GetVersionAsync(string recordingId, string documentId, string versionId, CancellationToken cancellationToken)
    {
        await LoadAsync(recordingId, documentId, cancellationToken);
        var version = await store.LoadVersionAsync(recordingId, documentId, versionId, cancellationToken) ?? throw M4Errors.VersionNotFound(versionId);
        var style = await styles.FindOrDefaultAsync(version.Document.StyleId, cancellationToken);
        return new DocumentVersionResult(M4Mapping.ToContent(version.Document), renderer.RenderViewer(version.Document, style).Html);
    }

    public async Task<DocumentRestoreResult> RestoreVersionAsync(string recordingId, string documentId, string versionId, CancellationToken cancellationToken)
    {
        await LoadAsync(recordingId, documentId, cancellationToken);
        var version = await store.LoadVersionAsync(recordingId, documentId, versionId, cancellationToken) ?? throw M4Errors.VersionNotFound(versionId);
        var saved = await store.WriteAsync(
            recordingId,
            documentId,
            DocumentChangeReasons.Restored,
            current => version.Document with { Name = current?.Name ?? version.Document.Name, CreatedAt = current?.CreatedAt ?? version.Document.CreatedAt },
            cancellationToken);
        await ChangedAsync(recordingId, documentId, DocumentChangeReasons.Restored, $"Document \"{saved!.Name ?? saved.Title}\" restored", $"Version from {version.SavedAt.ToString("g", CultureInfo.CurrentCulture)}", cancellationToken);
        return new DocumentRestoreResult(M4Mapping.ToContent(saved)) { Summary = await SummaryAsync(recordingId, saved, cancellationToken) };
    }

    /// <summary>
    /// Exports one document. Without a path the file goes to Settings › Export's folder, or Documents, named
    /// "{recording} {date} - {document}.{ext}" and never over an existing file.
    /// </summary>
    public async Task<DocumentFileResult> ExportAsync(string recordingId, string documentId, string format, string? path, CancellationToken cancellationToken)
    {
        var kind = ParseFormat(format);
        var manifest = await ManifestAsync(recordingId, cancellationToken);
        var document = await LoadAsync(recordingId, documentId, cancellationToken);
        var style = await styles.FindOrDefaultAsync(document.StyleId, cancellationToken);
        var target = Target(manifest, document, kind, path);
        DocumentExportResult result;
        try
        {
            result = await exporter.ExportToFileAsync(document, style, kind, target, cancellationToken);
        }
        catch (DocumentExportException ex)
        {
            throw M4Errors.ExportFailed(ex.Message, ex.Code);
        }

        await HistoryAsync(recordingId, $"Document \"{document.Name ?? document.Title}\" exported", $"{format} · {Path.GetFileName(target)} · {result.Length.ToString("N0", CultureInfo.InvariantCulture)} bytes", cancellationToken);
        return new DocumentFileResult(target, result.Length, result.Sha256);
    }

    public static DocumentExportFormat ParseFormat(string format) => format switch
    {
        "docx" => DocumentExportFormat.Docx,
        "pdf" => DocumentExportFormat.Pdf,
        "markdown" => DocumentExportFormat.Markdown,
        _ => throw M4Errors.Invalid($"format: '{format}' is not available. Choose docx, pdf or markdown.", "format"),
    };

    /// <summary>A file-name-safe version of a document name.</summary>
    public static string FileStem(string name)
    {
        var clean = UnsafeFileCharacters().Replace(name ?? string.Empty, " ").Trim().Trim('.');
        clean = MultipleSpaces().Replace(clean, " ");
        if (clean.Length > 80)
        {
            clean = clean[..80].Trim();
        }

        return clean.Length == 0 ? "Document" : clean;
    }

    /// <summary>Markup the viewer never produces, as a short phrase, or <c>null</c>.</summary>
    internal static string? Unsupported(string html)
    {
        if (ActiveContent().Match(html) is { Success: true } match)
        {
            return $"it contains {match.Value.Trim().ToLowerInvariant()} markup";
        }

        return EventHandler().IsMatch(html) ? "it contains script attributes" : null;
    }

    internal static int Changes(Document version, Document current)
    {
        var before = version.Modules().ToDictionary(m => m.Id, Json, StringComparer.Ordinal);
        var now = current.Modules().ToDictionary(m => m.Id, Json, StringComparer.Ordinal);
        return before.Keys.Union(now.Keys).Count(id => !before.TryGetValue(id, out var a) || !now.TryGetValue(id, out var b) || a != b)
            + (string.Equals(version.Title, current.Title, StringComparison.Ordinal) ? 0 : 1);
    }

    private static string Json(ModuleBlock module) => JsonSerializer.Serialize(module.Blocks, DocumentJsonContext.Default.IReadOnlyListBlock) + module.Title;

    private static bool Same(Document a, Document b) =>
        string.Equals(a.Title, b.Title, StringComparison.Ordinal) && Changes(a, b) == 0 && a.Rows.Count == b.Rows.Count;

    private string Target(ProjectManifest manifest, Document document, DocumentExportFormat format, string? path)
    {
        var extension = DocumentExporter.FileExtension(format);
        if (path is not null)
        {
            if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal) || Path.GetDirectoryName(path) is not { } folder || !Directory.Exists(folder))
            {
                throw new BridgeException(DomainErrorCodes.ExportDestinationUnwritable, "The document can only be exported to a full path in a folder that exists on this PC. Nothing was written; choose the folder again.", path);
            }

            // The path comes from the page: never over an existing file (a user's own .docx elsewhere), "name (2).docx" instead.
            var file = string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase) ? path : path + extension;
            var stemOf = Path.GetFileNameWithoutExtension(file);
            var unique = file;
            for (var n = 2; File.Exists(unique) || Directory.Exists(unique); n++)
            {
                unique = Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"{stemOf} ({n}){extension}"));
            }

            return unique;
        }

        var destination = settings.Current.Export.DefaultFolder is { Length: > 0 } configured && Directory.Exists(configured)
            ? configured
            : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var stem = ExportNaming.BaseName(manifest.Details.Title, manifest.CreatedAt) + " - " + FileStem(document.Name ?? document.Title);
        var candidate = Path.Combine(destination, stem + extension);
        for (var n = 2; File.Exists(candidate); n++)
        {
            candidate = Path.Combine(destination, string.Create(CultureInfo.InvariantCulture, $"{stem} ({n}){extension}"));
        }

        return candidate;
    }

    private async Task<DocumentSummary> SummaryAsync(string recordingId, Document document, CancellationToken cancellationToken)
    {
        // As documents.versions lists them: the current content and every kept version.
        var versions = settings.Current.History.KeepVersions ? (await store.ListVersionsAsync(recordingId, document.Id, cancellationToken)).Count + 1 : 0;
        return M4Mapping.ToSummary(document, versions, store.SizeOf(recordingId, document.Id));
    }

    private void EnsureProject(string recordingId)
    {
        if (!ProjectId.IsValid(recordingId) || !projects.Exists(recordingId))
        {
            throw M4Errors.ProjectNotFound(recordingId);
        }
    }

    private async Task<ProjectManifest> ManifestAsync(string recordingId, CancellationToken cancellationToken)
    {
        EnsureProject(recordingId);
        try
        {
            return await projects.LoadAsync(recordingId, cancellationToken);
        }
        catch (ProjectNotFoundException)
        {
            throw M4Errors.ProjectNotFound(recordingId);
        }
    }

    private static string CheckName(string? name)
    {
        var value = (name ?? string.Empty).Trim();
        if (value.Length is 0 or > MaxNameLength)
        {
            throw M4Errors.Invalid($"A document name needs 1 to {MaxNameLength} characters.", "name");
        }

        return value;
    }

    /// <summary><c>documents.changed</c>, the library row, and a History line when <paramref name="summary"/> is given.</summary>
    internal async Task ChangedAsync(string recordingId, string documentId, string reason, string? summary, string? detail, CancellationToken cancellationToken)
    {
        events.PublishDocumentsChanged(new DocumentsChangedPayload(recordingId, documentId, reason));
        try
        {
            await catalog.TouchedAsync(recordingId, cancellationToken);
        }
        catch (ProjectNotFoundException)
        {
            // Deleted meanwhile; the UI refetches anyway.
        }

        if (summary is not null)
        {
            await HistoryAsync(recordingId, summary, detail, cancellationToken);
        }
    }

    private async Task HistoryAsync(string recordingId, string summary, string? detail, CancellationToken cancellationToken)
    {
        try
        {
            await projects.AppendHistoryAsync(recordingId, new HistoryEntry(time.GetLocalNow(), "edited", "info", summary, detail), cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogHistoryFailed(ex, recordingId);
        }
    }

    [GeneratedRegex(@"<\s*(script|iframe|object|embed|form|img|svg|style|link|meta|video|audio|input|button|textarea)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ActiveContent();

    [GeneratedRegex(@"\son[a-z]+\s*=|javascript:", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EventHandler();

    [GeneratedRegex(@"[<>:""/\\|?*\u0000-\u001f]", RegexOptions.CultureInvariant)]
    private static partial Regex UnsafeFileCharacters();

    [GeneratedRegex(@"\s{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex MultipleSpaces();

    [LoggerMessage(Level = LogLevel.Warning, Message = "History of recording {RecordingId} could not be appended")]
    private partial void LogHistoryFailed(Exception exception, string recordingId);
}
