using System.IO.Compression;
using System.Text;
using Memento.Documents.Agenda;
using Memento.Documents.Agenda.Ocr;
using Memento.Documents.Agenda.OpenXml;
using Memento.Documents.Agenda.Pdf;
using Memento.Documents.Agenda.Tables;
using Memento.Documents.Agenda.Text;
using Memento.Documents.Tests.Support;
using UglyToad.PdfPig.Writer;

namespace Memento.Documents.Tests;

/// <summary>The importer decides by content, never by the extension alone.</summary>
public sealed class SniffingTests
{
    [Fact]
    public async Task ADocxThatIsReallyAPngIsReadAsAnImage()
    {
        var engine = new FakeOcrEngine("fake", available: true, FakeOcrEngine.Lines("Agenda", "1. Welcome", "2. Close"));
        var result = await Agendas.ImportAsync(Agendas.Fixture("ocr-small.png"), "agenda.docx", Agendas.Importer(engine));

        Assert.Equal(AgendaSourceKind.Image, result.Source);
        Assert.Equal(1, engine.Calls);
        var warning = result.Warnings[0];
        Assert.Equal(AgendaWarningCodes.ExtensionMismatch, warning.Code);
        Assert.Contains("agenda.docx", warning.Message, StringComparison.Ordinal);
        Assert.Contains("PNG image", warning.Message, StringComparison.Ordinal);
        Assert.Equal(["0|Welcome", "0|Close"], Agendas.Shape(result));
    }

    [Fact]
    public async Task AWordDocumentNamedXlsxIsReadAsWord()
    {
        var result = await Agendas.ImportAsync(Agendas.Fixture("board-table.docx"), "agenda.xlsx");

        Assert.Equal(AgendaSourceKind.Docx, result.Source);
        Assert.Equal(AgendaWarningCodes.ExtensionMismatch, result.Warnings[0].Code);
    }

    [Fact]
    public async Task TextNamedPdfIsReadAsText()
    {
        var result = await Agendas.ImportTextAsync("1. Welcome\n2. Close", "agenda.pdf");

        Assert.Equal(AgendaSourceKind.Text, result.Source);
        Assert.Equal(AgendaWarningCodes.ExtensionMismatch, result.Warnings[0].Code);
    }

    [Fact]
    public async Task TextWithoutAUsefulNameIsSniffedForMarkdownAndTabs()
    {
        var markdown = await Agendas.ImportTextAsync("# Sync\n\n- [ ] Status\n- [ ] Hiring", "agenda");
        var tabs = await Agendas.ImportTextAsync("Topic\tLead\nWelcome\tChair\nBudget\tTreasurer", "clipboard");

        Assert.Equal(AgendaSourceKind.Markdown, markdown.Source);
        Assert.Equal(AgendaSourceKind.Tsv, tabs.Source);
    }

    [Fact]
    public async Task AMarkdownFileStaysMarkdownAndACsvStaysCsv()
    {
        Assert.Equal(AgendaSourceKind.Markdown, (await Agendas.ImportTextAsync("- Welcome\n- Close", "notes.md")).Source);
        Assert.Equal(AgendaSourceKind.Csv, (await Agendas.ImportTextAsync("Topic\nWelcome\nClose", "agenda.csv")).Source);
    }

    [Theory]
    [InlineData(new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0, 0, 0, 0 }, "agenda.doc", "older Word or Excel file")]
    [InlineData(new byte[] { (byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a', 1, 0, 1, 0, 0, 0 }, "agenda.gif", "GIF image")]
    [InlineData(new byte[] { (byte)'{', (byte)'\\', (byte)'r', (byte)'t', (byte)'f', (byte)'1' }, "agenda.rtf", "Rich Text")]
    [InlineData(new byte[] { 0x00, 0x01, 0x02, 0x03, 0xFF, 0xFE, 0x00, 0x00, 0x10 }, "agenda.bin", "not a format")]
    public async Task UnsupportedContentIsASpecificError(byte[] bytes, string name, string what)
    {
        var error = await Assert.ThrowsAsync<AgendaImportException>(() => Agendas.ImportAsync(bytes, name));

        Assert.Equal(AgendaErrorCodes.UnsupportedFormat, error.Code);
        Assert.Contains(what, error.Message, StringComparison.Ordinal);
        Assert.Contains("Nothing was imported", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APowerPointPackageIsRefusedWithAFix()
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            zip.CreateEntry("ppt/presentation.xml");
        }

        var error = await Assert.ThrowsAsync<AgendaImportException>(() => Agendas.ImportAsync(buffer.ToArray(), "agenda.docx"));

        Assert.Equal(AgendaErrorCodes.UnsupportedFormat, error.Code);
        Assert.Contains("PowerPoint", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnEncryptedOfficeFileIsReportedAsProtected()
    {
        var bytes = new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }.Concat(Encoding.Unicode.GetBytes("EncryptionInfo")).ToArray();

        var error = await Assert.ThrowsAsync<AgendaImportException>(() => Agendas.ImportAsync(bytes, "agenda.docx"));

        Assert.Equal(AgendaErrorCodes.Protected, error.Code);
    }

    [Fact]
    public async Task ADamagedWordFileIsUnreadable()
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(zip.CreateEntry("word/document.xml").Open());
            writer.Write("<not-really-word/>");
        }

        var error = await Assert.ThrowsAsync<AgendaImportException>(() => Agendas.ImportAsync(buffer.ToArray(), "agenda.docx"));

        Assert.Equal(AgendaErrorCodes.Unreadable, error.Code);
        Assert.Contains("Word document", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APdfWithoutTextSaysItMayBeAScan()
    {
        var builder = new PdfDocumentBuilder();
        builder.AddPage(595, 842);

        var error = await Assert.ThrowsAsync<AgendaImportException>(() => Agendas.ImportAsync(builder.Build(), "scan.pdf"));

        Assert.Equal(AgendaErrorCodes.NoText, error.Code);
        Assert.Contains("scan", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnEmptyFileIsASpecificError()
    {
        var error = await Assert.ThrowsAsync<AgendaImportException>(() => Agendas.ImportAsync([], "agenda.txt"));

        Assert.Equal(AgendaErrorCodes.NoText, error.Code);
        Assert.Contains("agenda.txt is empty", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParsersClaimTheirExtensionsAndContentTypes()
    {
        Assert.True(new PlainTextAgendaParser().CanParse("a.txt", null));
        Assert.True(new MarkdownAgendaParser().CanParse("a.MD", null));
        Assert.True(new DelimitedAgendaParser().CanParse("a.tsv", null));
        Assert.True(new DelimitedAgendaParser().CanParse("pasted", "text/csv; charset=utf-8"));
        Assert.True(new DocxAgendaParser().CanParse("a.docx", null));
        Assert.True(new XlsxAgendaParser().CanParse("x", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"));
        Assert.True(new PdfAgendaParser().CanParse("a.pdf", null));
        Assert.True(new ImageAgendaParser([]).CanParse("photo.HEIC", null));
        Assert.False(new PdfAgendaParser().CanParse("a.docx", "application/msword"));
    }

    [Fact]
    public void EveryKindHasABuiltInParser()
    {
        var parsers = AgendaImporter.CreateParsers([]);

        foreach (var kind in Enum.GetValues<AgendaSourceKind>())
        {
            Assert.Contains(parsers, p => p.Kinds.Contains(kind));
        }
    }
}
