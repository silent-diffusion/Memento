using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Memento.Documents.Agenda;
using Memento.Documents.Agenda.Pdf;

namespace Memento.Documents.Tests.Hardening;

/// <summary>A scanned PDF's pages are rendered at no more than 6,000 px a side and 36 megapixels, whatever its MediaBox says.</summary>
public sealed class PdfRenderSizeTests
{
    [Theory]
    [InlineData(816, 1056)]
    [InlineData(1e9, 1e9)]
    [InlineData(1e9, 1)]
    [InlineData(19_200, 19_200)]
    public void ARenderedPageStaysWithinThePixelLimits(double width, double height)
    {
        var (w, h) = WindowsPdfPageRenderer.RenderSize(width, height, 200)!.Value;

        Assert.InRange(w, 1u, (uint)WindowsPdfPageRenderer.MaxSide);
        Assert.InRange(h, 1u, (uint)WindowsPdfPageRenderer.MaxSide);
        Assert.True((double)w * h <= WindowsPdfPageRenderer.MaxPixels, $"{w} x {h}");
    }

    [Fact]
    public void AnOrdinaryPageIsRenderedAtTheRequestedResolution()
    {
        // US Letter is 816 x 1056 device-independent pixels; at 200 dpi that is 1700 x 2200.
        Assert.Equal((1700u, 2200u), WindowsPdfPageRenderer.RenderSize(816, 1056, 200));
    }

    [Theory]
    [InlineData(double.NaN, 100)]
    [InlineData(100, double.PositiveInfinity)]
    [InlineData(0, 100)]
    [InlineData(-5, 100)]
    public void APageWithNoUsableSizeIsSkipped(double width, double height)
    {
        Assert.Null(WindowsPdfPageRenderer.RenderSize(width, height, 200));
    }

    [Fact]
    public async Task APageWithAHugeMediaBoxIsRenderedScaledDown()
    {
        var pdf = BlankPdf("[0 0 14400 14400]");

        var pages = await new WindowsPdfPageRenderer().RenderAsync(pdf, 200, 1, AgendaParseOptions.Default, CancellationToken.None);

        var png = Assert.Single(pages);
        var width = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(16));
        var height = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(20));
        Assert.True(width <= 6_000 && height <= 6_000 && (double)width * height <= 36_000_000, $"{width} x {height}");
    }

    /// <summary>A one-page PDF with no content and the given MediaBox, with a correct cross-reference table.</summary>
    private static byte[] BlankPdf(string mediaBox)
    {
        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox {mediaBox} /Contents 4 0 R >>",
            "<< /Length 0 >>\nstream\n\nendstream",
        ];
        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(pdf.Length);
            pdf.Append(CultureInfo.InvariantCulture, $"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        var xref = pdf.Length;
        pdf.Append(CultureInfo.InvariantCulture, $"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            pdf.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        }

        pdf.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }
}
