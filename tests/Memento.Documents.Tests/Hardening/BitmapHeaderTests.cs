using System.Buffers.Binary;
using System.Text;
using Memento.Documents.Agenda;
using Memento.Documents.Agenda.Ocr;
using Memento.Documents.Tests.Support;

namespace Memento.Documents.Tests.Hardening;

/// <summary>Bitmap headers are measured without overflow, and only real bitmaps are taken for one.</summary>
public sealed class BitmapHeaderTests
{
    [Theory]
    [InlineData(int.MinValue, 100)]
    [InlineData(100, int.MinValue)]
    [InlineData(-20_000, 100)]
    public async Task ABitmapHeaderWithAnExtremeSizeIsRefusedNotOverflowed(int width, int height)
    {
        var engine = new FakeOcrEngine("fake", available: true, FakeOcrEngine.Lines("Welcome"));

        var error = await Assert.ThrowsAsync<AgendaImportException>(() => Agendas.ImportAsync(BmpHeader(40, width, height), "scan.bmp", Agendas.Importer(engine)));

        Assert.Equal(AgendaErrorCodes.ImageTooLarge, error.Code);
        Assert.Equal(0, engine.Calls);
    }

    [Fact]
    public void AnOldStyleBitmapHeaderHasSixteenBitSizes()
    {
        Assert.True(ImageFormats.TryGetSize(BmpHeader(12, 0x0300_0200, 0), out var width, out var height));

        Assert.Equal((0x0200, 0x0300), (width, height));
    }

    [Fact]
    public async Task TextThatStartsWithBMIsText()
    {
        var result = await Agendas.ImportTextAsync("BMW supplier review\n1. Welcome\n2. Delivery dates\n3. Next steps", "agenda.txt");

        Assert.Equal(AgendaSourceKind.Text, result.Source);
        Assert.Null(ImageFormats.Detect(Encoding.ASCII.GetBytes("BM followed by text that is long enough")));
    }

    private static byte[] BmpHeader(uint headerSize, int width, int height)
    {
        var bytes = new byte[64];
        bytes[0] = (byte)'B';
        bytes[1] = (byte)'M';
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(14), headerSize);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(18), width);
        if (headerSize != 12)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(22), height);
        }

        return bytes;
    }
}
