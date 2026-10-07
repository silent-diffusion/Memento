using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Data.Pdf;
using Windows.Storage.Streams;

namespace Memento.Documents.Agenda.Pdf;

/// <summary>
/// <see cref="IPdfPageRenderer"/> with the PDF renderer built into Windows (<c>Windows.Data.Pdf</c>, from the Windows
/// SDK projection of the TFM); no extra dependency. Pages are rendered on a white background as PNG.
/// </summary>
public sealed class WindowsPdfPageRenderer : IPdfPageRenderer
{
    /// <summary>Windows reports page sizes in device-independent pixels (1/96 inch).</summary>
    private const double PageDpi = 96.0;

    /// <summary>The longest side of a rendered page in pixels (A3 at 200 dpi is about 3,300).</summary>
    private const double MaxSide = 10_000;

    public async Task<IReadOnlyList<byte[]>> RenderAsync(ReadOnlyMemory<byte> pdf, int dpi, int maxPages, AgendaParseOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        // The document reads its pages from the stream while rendering, so the stream lives as long as this call.
        using var source = new MemoryStream(pdf.ToArray(), writable: false);
        using var stream = source.AsRandomAccessStream();
        PdfDocument document;
        try
        {
            document = await PdfDocument.LoadFromStreamAsync(stream).AsTask(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is COMException or ArgumentException or InvalidOperationException)
        {
            throw AgendaErrors.Unreadable(options, "a PDF", e);
        }

        var pages = new List<byte[]>();
        var count = (int)Math.Min(document.PageCount, (uint)maxPages);
        for (var index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var page = document.GetPage((uint)index);
            // A page with a huge MediaBox must not become a multi-gigabyte bitmap: the longer side stays within MaxSide.
            var scale = Math.Min(dpi / PageDpi, MaxSide / Math.Max(1.0, Math.Max(page.Size.Width, page.Size.Height)));
            var render = new PdfPageRenderOptions
            {
                DestinationWidth = (uint)Math.Max(1, Math.Round(page.Size.Width * scale)),
                DestinationHeight = (uint)Math.Max(1, Math.Round(page.Size.Height * scale)),
                BackgroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255),
            };
            using var output = new InMemoryRandomAccessStream();
            try
            {
                await page.RenderToStreamAsync(output, render).AsTask(cancellationToken).ConfigureAwait(false);
            }
            catch (COMException e)
            {
                throw AgendaErrors.Unreadable(options, "a PDF", e);
            }

            output.Seek(0);
            using var png = new MemoryStream((int)output.Size);
            using (var input = output.AsStreamForRead())
            {
                await input.CopyToAsync(png, cancellationToken).ConfigureAwait(false);
            }

            pages.Add(png.ToArray());
        }

        return pages;
    }
}
