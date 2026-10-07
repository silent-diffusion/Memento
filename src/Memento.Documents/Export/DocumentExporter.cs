using System.Security.Cryptography;
using System.Text;
using Memento.Documents.Model;
using Memento.Documents.Model.Storage;
using Memento.Documents.Render;
using Memento.Documents.Styling;

namespace Memento.Documents.Export;

/// <summary>
/// The one entry point for document export: picks the exporter by format and returns the bytes with their SHA-256.
/// Word and PDF follow the style; Markdown ignores it.
/// </summary>
public sealed class DocumentExporter
{
    private readonly DocxExporter _docx;
    private readonly MarkdownExporter _markdown;
    private readonly DocumentHtmlRenderer _renderer;
    private readonly IPdfPrinter? _pdfPrinter;

    public DocumentExporter(IPdfPrinter? pdfPrinter = null, DocxExporter? docx = null, MarkdownExporter? markdown = null, DocumentHtmlRenderer? renderer = null)
    {
        _pdfPrinter = pdfPrinter;
        _docx = docx ?? new DocxExporter();
        _markdown = markdown ?? new MarkdownExporter();
        _renderer = renderer ?? new DocumentHtmlRenderer();
    }

    public static string FileExtension(DocumentExportFormat format) => format switch
    {
        DocumentExportFormat.Docx => ".docx",
        DocumentExportFormat.Pdf => ".pdf",
        _ => ".md",
    };

    public static string MediaType(DocumentExportFormat format) => format switch
    {
        DocumentExportFormat.Docx => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        DocumentExportFormat.Pdf => "application/pdf",
        _ => "text/markdown; charset=utf-8",
    };

    /// <exception cref="DocumentExportException">PDF without a printer, or the printer failed.</exception>
    public async Task<DocumentExportResult> ExportAsync(Document document, DocumentStyle style, DocumentExportFormat format, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(style);
        cancellationToken.ThrowIfCancellationRequested();
        byte[] bytes;
        switch (format)
        {
            case DocumentExportFormat.Docx:
                bytes = _docx.Export(document, style);
                break;
            case DocumentExportFormat.Markdown:
                bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(_markdown.Export(document));
                break;
            case DocumentExportFormat.Pdf:
                if (_pdfPrinter is null)
                {
                    throw new DocumentExportException(
                        DocumentExportErrorCodes.PdfPrinterUnavailable,
                        $"\"{document.Title}\" could not be saved as PDF because the PDF printer is not available. The document is unchanged; export it as Word or Markdown instead.");
                }

                var html = _renderer.RenderPrintHtml(document, style);
                try
                {
                    bytes = await _pdfPrinter.PrintAsync(html, PdfPrintOptions.For(style), cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException and not DocumentExportException)
                {
                    throw new DocumentExportException(
                        DocumentExportErrorCodes.PdfFailed,
                        $"\"{document.Title}\" could not be printed to PDF ({ex.Message}). The document is unchanged; try again, or export it as Word.",
                        ex);
                }

                if (bytes.Length == 0)
                {
                    throw new DocumentExportException(
                        DocumentExportErrorCodes.PdfFailed,
                        $"Printing \"{document.Title}\" to PDF produced an empty file. The document is unchanged; try again, or export it as Word.");
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown document export format.");
        }

        return new DocumentExportResult(format, bytes, Sha256Hex(bytes), FileExtension(format), MediaType(format));
    }

    /// <summary>Exports and writes atomically to <paramref name="path"/>.</summary>
    /// <exception cref="DocumentExportException">The file could not be written.</exception>
    public async Task<DocumentExportResult> ExportToFileAsync(Document document, DocumentStyle style, DocumentExportFormat format, string path, CancellationToken cancellationToken)
    {
        var result = await ExportAsync(document, style, format, cancellationToken);
        try
        {
            await AtomicFile.WriteAllBytesAsync(path, result.Content, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new DocumentExportException(
                DocumentExportErrorCodes.WriteFailed,
                $"\"{document.Title}\" could not be written to {Path.GetFileName(path)} ({ex.Message}). The document is unchanged; free some space or choose another folder.",
                ex);
        }

        return result;
    }

    public static string Sha256Hex(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
