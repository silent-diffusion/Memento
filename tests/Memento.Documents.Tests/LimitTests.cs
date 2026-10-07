using System.Buffers.Binary;
using System.Text;
using Memento.Documents.Agenda;
using Memento.Documents.Tests.Support;

namespace Memento.Documents.Tests;

/// <summary>Files over 25 MB and images over 10,000 px a side are refused before parsing, with a specific error.</summary>
public sealed class LimitTests
{
    [Fact]
    public async Task AFileOver25MegabytesIsRefusedBeforeItIsRead()
    {
        using var stream = new LengthOnlyStream(26L * 1024 * 1024);

        var error = await Assert.ThrowsAsync<AgendaImportException>(() =>
            Agendas.Importer().ImportAsync(stream, new AgendaParseOptions { FileName = "big.pdf" }, CancellationToken.None));

        Assert.Equal(AgendaErrorCodes.FileTooLarge, error.Code);
        Assert.Equal(
            "big.pdf is 26 MB, larger than the 25 MB limit for an agenda. Nothing was imported. Save just the agenda pages in a smaller file, or paste the items as text.",
            error.Message);
        Assert.Equal(0, stream.BytesRead);
    }

    [Fact]
    public async Task AStreamThatKeepsGoingIsCutOffAtTheLimit()
    {
        using var stream = new UnseekableStream(new MemoryStream(new byte[5000]));

        var error = await Assert.ThrowsAsync<AgendaImportException>(() =>
            Agendas.Importer().ImportAsync(stream, new AgendaParseOptions { FileName = "agenda.txt", MaxFileBytes = 4096 }, CancellationToken.None));

        Assert.Equal(AgendaErrorCodes.FileTooLarge, error.Code);
    }

    [Fact]
    public async Task AFileOnDiskIsCheckedByItsSize()
    {
        var path = Path.Combine(Path.GetTempPath(), $"memento-agenda-{Guid.NewGuid():N}.docx");
        try
        {
            using (var file = File.Create(path))
            {
                file.SetLength(AgendaLimits.MaxFileBytes + 1);
            }

            var error = await Assert.ThrowsAsync<AgendaImportException>(() => Agendas.Importer().ImportFileAsync(path, null, CancellationToken.None));

            Assert.Equal(AgendaErrorCodes.FileTooLarge, error.Code);
            Assert.StartsWith(Path.GetFileName(path), error.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task PastedTextOverTheLimitIsRefused()
    {
        var error = await Assert.ThrowsAsync<AgendaImportException>(() =>
            Agendas.PasteAsync(new string('a', 3000), new AgendaParseOptions { MaxFileBytes = 2048 }));

        Assert.Equal(AgendaErrorCodes.FileTooLarge, error.Code);
        Assert.StartsWith("The agenda is", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(12_000, 800)]
    [InlineData(800, 10_001)]
    public async Task AnImageOverTenThousandPixelsASideIsRefusedBeforeDecoding(int width, int height)
    {
        var engine = new FakeOcrEngine("fake", available: true, FakeOcrEngine.Lines("Welcome"));

        var error = await Assert.ThrowsAsync<AgendaImportException>(() => Agendas.ImportAsync(PngHeader(width, height), "huge.png", Agendas.Importer(engine)));

        Assert.Equal(AgendaErrorCodes.ImageTooLarge, error.Code);
        Assert.Contains($"{width:N0} × {height:N0} pixels", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, engine.Calls);
    }

    [Fact]
    public async Task AJpegHeaderIsMeasuredToo()
    {
        var engine = new FakeOcrEngine("fake", available: true, FakeOcrEngine.Lines("Welcome"));
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x04, 0x00, 0x00, 0xFF, 0xC0, 0x00, 0x11, 0x08, 0x2E, 0xE0, 0x03, 0x20, 0x03, 0x01, 0x22, 0x00];

        var error = await Assert.ThrowsAsync<AgendaImportException>(() => Agendas.ImportAsync(jpeg, "photo.jpg", Agendas.Importer(engine)));

        Assert.Equal(AgendaErrorCodes.ImageTooLarge, error.Code);
    }

    private static byte[] PngHeader(int width, int height)
    {
        var bytes = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13 }.CopyTo(bytes, 0);
        Encoding.ASCII.GetBytes("IHDR").CopyTo(bytes, 12);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20), (uint)height);
        return bytes;
    }

    /// <summary>A seekable stream that only reports a length; reading it is a test failure.</summary>
    private sealed class LengthOnlyStream(long length) : Stream
    {
        public long BytesRead { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => length;

        public override long Position { get; set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            BytesRead += count;
            return count;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => Position = offset;

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
