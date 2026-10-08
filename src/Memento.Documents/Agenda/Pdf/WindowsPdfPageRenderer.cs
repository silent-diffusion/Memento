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

    /// <summary>The longest side a page is rendered at: an A3 page at 200 dpi is about 3,300 px.</summary>
    internal const double MaxSide = 6_000;

    /// <summary>The most pixels a page is rendered with (36 megapixels, about 144 MB as BGRA).</summary>
    internal const double MaxPixels = 36_000_000;

    /// <summary>A rendered page's PNG larger than this is not a page of text.</summary>
    private const ulong MaxPngBytes = 256UL * 1024 * 1024;

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
            PdfPage page;
            Windows.Foundation.Size size;
            try
            {
                page = document.GetPage((uint)index);
                size = page.Size;
            }
            catch (Exception e) when (e is COMException or ArgumentException or InvalidOperationException)
            {
                throw AgendaErrors.Unreadable(options, "a PDF", e);
            }

            using (page)
            {
                if (RenderSize(size.Width, size.Height, dpi) is not { } pixels)
                {
                    // A page with no usable size (zero, negative or not a number) has nothing to read.
                    continue;
                }

                var render = new PdfPageRenderOptions
                {
                    DestinationWidth = pixels.Width,
                    DestinationHeight = pixels.Height,
                    BackgroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255),
                };
                using var output = new InMemoryRandomAccessStream();
                try
                {
                    await page.RenderToStreamAsync(output, render).AsTask(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e) when (e is COMException or ArgumentException or InvalidOperationException)
                {
                    throw AgendaErrors.Unreadable(options, "a PDF", e);
                }

                if (output.Size > MaxPngBytes)
                {
                    throw AgendaErrors.Unreadable(options, "a PDF");
                }

                output.Seek(0);
                using var png = new MemoryStream((int)output.Size);
                using (var input = output.AsStreamForRead())
                {
                    await input.CopyToAsync(png, cancellationToken).ConfigureAwait(false);
                }

                pages.Add(png.ToArray());
            }
        }

        return pages;
    }

    /// <summary>
    /// The pixel size to render a page of <paramref name="width"/> × <paramref name="height"/> device-independent
    /// pixels at <paramref name="dpi"/>, scaled down to at most <see cref="MaxSide"/> a side and <see cref="MaxPixels"/>
    /// in all (a damaged MediaBox can claim a page kilometres wide); <c>null</c> for a size that is not a page.
    /// </summary>
    internal static (uint Width, uint Height)? RenderSize(double width, double height, int dpi)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0 || dpi <= 0)
        {
            return null;
        }

        var scale = dpi / PageDpi;
        var w = width * scale;
        var h = height * scale;
        var fit = Math.Min(1.0, Math.Min(MaxSide / w, MaxSide / h));
        fit = Math.Min(fit, Math.Sqrt(MaxPixels / (w * h)));
        w = Math.Clamp(Math.Floor(w * fit), 1, MaxSide);
        h = Math.Clamp(Math.Floor(h * fit), 1, MaxSide);
        return ((uint)w, (uint)h);
    }
}
