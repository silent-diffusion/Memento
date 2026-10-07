namespace Memento.Documents.Export;

/// <summary>
/// Prints the print HTML (<see cref="Render.DocumentHtmlRenderer.RenderPrintHtml"/>) to PDF. The host implements it with
/// WebView2 <c>PrintToPdfAsync</c> on an offscreen WebView2 that loads the HTML from a temporary file with scripts off and
/// <see cref="PrintPagePolicy"/> applied, so the PDF comes from the same markup the viewer shows and nothing else is fetched.
/// </summary>
public interface IPdfPrinter
{
    /// <summary>The PDF bytes. Throws when printing fails; the caller reports it with the document name.</summary>
    Task<byte[]> PrintAsync(string html, PdfPrintOptions options, CancellationToken cancellationToken);
}
