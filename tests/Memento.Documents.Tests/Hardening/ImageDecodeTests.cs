using System.Drawing;
using System.Drawing.Imaging;
using Memento.Documents.Agenda;
using Memento.Documents.Agenda.Ocr;

namespace Memento.Documents.Tests.Hardening;

/// <summary>Windows decodes no more pixels than the recognizer can use, and its decoder errors are agenda errors.</summary>
public sealed class ImageDecodeTests
{
    [Fact]
    public void TheDecodeScaleKeepsAPhotoUnderFortyMegapixels()
    {
        var scale = WindowsOcrEngine.MaxScale(10_000, 10_000, 10_000);

        Assert.True(10_000 * scale * 10_000 * scale <= WindowsOcrEngine.MaxImagePixels + 1);
        Assert.Equal(1.0, Math.Min(1.0, WindowsOcrEngine.MaxScale(2_000, 1_500, 10_000)));
        Assert.Equal(0.5, WindowsOcrEngine.MaxScale(20_000, 10, 10_000));
    }

    [Fact]
    public void TheRotatedCanvasIsMeasuredBeforeItIsMade()
    {
        Assert.Equal((500, 1000), GrayImage.RotatedSize(1000, 500, 90));
        Assert.Equal((1000, 500), GrayImage.RotatedSize(1000, 500, 0));
        Assert.True(GrayImage.RotatedSize(1000, 1000, 45).Width > 1400);
    }

    [Fact]
    public async Task ALargePhotoIsDecodedScaledDown()
    {
        var png = BlankPng(8_000, 6_000);

        var (image, scale) = await WindowsOcrEngine.DecodeAsync(png, 10_000, 10_000, CancellationToken.None);

        Assert.True((long)image.Width * image.Height <= WindowsOcrEngine.MaxImagePixels, $"{image.Width} x {image.Height}");
        Assert.True(scale < 1.0);
    }

    [Fact]
    public async Task ATruncatedImageIsUnreadableNotAComError()
    {
        var png = BlankPng(400, 300);

        var error = await Assert.ThrowsAsync<AgendaImportException>(() => WindowsOcrEngine.DecodeAsync(png.AsMemory(0, png.Length / 2), 10_000, 10_000, CancellationToken.None));

        Assert.Equal(AgendaErrorCodes.Unreadable, error.Code);
    }

    private static byte[] BlankPng(int width, int height)
    {
        using var bitmap = new Bitmap(width, height, PixelFormat.Format1bppIndexed);
        using var output = new MemoryStream();
        bitmap.Save(output, ImageFormat.Png);
        return output.ToArray();
    }
}
