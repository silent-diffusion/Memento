using Memento.Core.Bridge.Contracts;
using Memento.Core.Export;
using Memento.Core.Host;
using Memento.Documents.Export;
using Memento.Documents.Render;
using Memento.Generation.Bridge;

namespace Memento.Generation.Documents;

/// <summary>
/// <c>documents.copy</c> (after 1.2.0): one document on the Windows clipboard twice over, as the Markdown its export writes
/// (every program can paste it) and as the page its PDF is printed from (Word and Outlook paste it formatted).
/// <c>documents.copyModule</c> (2.0): one module, such as the Follow-up email, as plain text and as its formatted blocks
/// without heading or timestamps, ready to paste into an email. Reads only.
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

    /// <exception cref="Core.Bridge.BridgeException"><c>documents.notFound</c>, or <c>bridge.invalidParams</c> (detail <c>moduleId</c>) for a module the document does not have.</exception>
    public async Task<DocumentCopyResult> CopyModuleAsync(string recordingId, string documentId, string moduleId, CancellationToken cancellationToken)
    {
        var document = await documents.LoadAsync(recordingId, documentId, cancellationToken);
        var module = document.Modules().FirstOrDefault(m => string.Equals(m.Id, moduleId, StringComparison.Ordinal))
            ?? throw M4Errors.Invalid($"“{document.Name ?? document.Title}” has no module '{moduleId}'. Nothing was copied.", "moduleId");
        var style = await styles.FindOrDefaultAsync(document.StyleId, cancellationToken);
        var text = ModulePlainText.Write(module);
        var html = renderer.RenderModuleHtml(document, module, style);
        await ClipboardWrite.SetAsync(clipboard, new ClipboardContent(text, html), $"the {module.Title}", cancellationToken);
        return new DocumentCopyResult(text.Length, Formatted: true);
    }
}
