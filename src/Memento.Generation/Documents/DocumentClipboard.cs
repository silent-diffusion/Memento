using Memento.Core.Bridge.Contracts;
using Memento.Core.Export;
using Memento.Core.Host;
using Memento.Documents.Export;
using Memento.Documents.Render;

namespace Memento.Generation.Documents;

/// <summary>
/// <c>documents.copy</c> (after 1.2.0): one document on the Windows clipboard twice over, as the Markdown its export writes
/// (every program can paste it) and as the page its PDF is printed from (Word and Outlook paste it formatted). Reads only.
/// </summary>
public sealed class DocumentClipboard(DocumentService documents, StyleService styles, DocumentHtmlRenderer renderer, MarkdownExporter markdown, IClipboard clipboard)
{
    public async Task<DocumentCopyResult> CopyAsync(string recordingId, string documentId, CancellationToken cancellationToken)
    {
        var document = await documents.LoadAsync(recordingId, documentId, cancellationToken);
        var style = await styles.FindOrDefaultAsync(document.StyleId, cancellationToken);
        var text = markdown.Export(document);
        var html = renderer.RenderPrintHtml(document, style);
        await ClipboardWrite.SetAsync(clipboard, new ClipboardContent(text, html), $"“{document.Name ?? document.Title}”", cancellationToken);
        return new DocumentCopyResult(text.Length, Formatted: true);
    }
}
