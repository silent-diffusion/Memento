using System.Globalization;
using Memento.Core.Documents;
using Memento.Documents.Export;
using Memento.Documents.Model;
using Memento.Documents.Render;

namespace Memento.Generation.Documents;

/// <summary>
/// The Export dialog's Documents row (Core's <see cref="IDocumentExportSource"/>): each chosen document (all of them when
/// none is named) in Word, PDF or Markdown, named after the document. Word and Markdown are rendered now, so their sizes
/// are exact; PDF is printed when the file is written.
/// </summary>
public sealed class DocumentExportSource(ProjectDocumentStore store, StyleService styles, DocumentExporter exporter, DocumentHtmlRenderer renderer) : IDocumentExportSource
{
    public async Task<DocumentExportPlan> PlanAsync(string recordingId, IReadOnlyList<string> documentIds, string format, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(documentIds);
        var kind = format switch
        {
            "pdf" => DocumentExportFormat.Pdf,
            "markdown" => DocumentExportFormat.Markdown,
            _ => DocumentExportFormat.Docx,
        };
        var all = await store.ListAsync(recordingId, cancellationToken);
        var chosen = documentIds.Count == 0 ? all.OrderBy(d => d.CreatedAt).ToList() : documentIds.Select(id => all.FirstOrDefault(d => d.Id == id)).OfType<Document>().ToList();
        if (chosen.Count == 0)
        {
            return new DocumentExportPlan([], all.Count == 0 ? "No documents yet" : "The chosen documents are no longer in this recording");
        }

        var files = new List<DocumentExportFile>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var document in chosen)
        {
            var style = await styles.FindOrDefaultAsync(document.StyleId, cancellationToken);
            var stem = DocumentService.FileStem(document.Name ?? document.Title);
            var name = stem + DocumentExporter.FileExtension(kind);
            for (var n = 2; !names.Add(name); n++)
            {
                name = string.Create(CultureInfo.InvariantCulture, $"{stem} ({n}){DocumentExporter.FileExtension(kind)}");
            }

            if (kind == DocumentExportFormat.Pdf)
            {
                var estimate = renderer.RenderPrintHtml(document, style).Length;
                files.Add(new DocumentExportFile(name, estimate, async (path, ct) =>
                {
                    var result = await exporter.ExportAsync(document, style, kind, ct);
                    await File.WriteAllBytesAsync(path, result.Content.ToArray(), ct);
                }));
                continue;
            }

            var bytes = (await exporter.ExportAsync(document, style, kind, cancellationToken)).Content;
            files.Add(new DocumentExportFile(name, bytes.Length, (path, ct) => File.WriteAllBytesAsync(path, bytes.ToArray(), ct)));
        }

        return new DocumentExportPlan(files, null);
    }
}
