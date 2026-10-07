namespace Memento.Documents.Agenda.Pdf;

/// <summary>Renders PDF pages to images, so a scanned agenda (no text layer) can be read with text recognition.</summary>
public interface IPdfPageRenderer
{
    /// <summary>The first <paramref name="maxPages"/> pages as PNG images at <paramref name="dpi"/>.</summary>
    /// <exception cref="AgendaImportException"><c>agenda.unreadable</c> when the PDF cannot be rendered.</exception>
    Task<IReadOnlyList<byte[]>> RenderAsync(ReadOnlyMemory<byte> pdf, int dpi, int maxPages, AgendaParseOptions options, CancellationToken cancellationToken);
}
