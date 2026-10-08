using System.Text;
using Memento.Documents.Agenda;
using Memento.Documents.Agenda.OpenXml;
using Memento.Documents.Tests.Support;

namespace Memento.Documents.Tests.Hardening;

/// <summary>
/// The parser contract: a damaged file fails with <c>agenda.unreadable</c> (never an XML, ZIP, format or index error
/// from the libraries underneath), and attribute values Word or Excel would ignore are ignored here too.
/// </summary>
public sealed class ParserContractTests
{
    [Theory]
    [InlineData("<w:tcPr><w:gridSpan w:val=\"x\"/>")]
    [InlineData("<w:tcPr><w:gridSpan w:val=\"99999999999\"/>")]
    [InlineData("<w:tcPr><w:vMerge w:val=\"bogus\"/>")]
    public async Task ABadTableCellValueInAWordTableIsIgnored(string cellProperties)
    {
        var docx = PackageParts.WithPart(Agendas.Fixture("board-table.docx"), "word/document.xml", xml => xml.Replace("<w:tc>", "<w:tc>" + cellProperties + "</w:tcPr>", StringComparison.Ordinal));

        var result = await new DocxAgendaParser().ParseAsync(new MemoryStream(docx), Options("board.docx"), CancellationToken.None);

        Assert.Contains(result.Items, i => i.Text == "Opening and quorum");
    }

    [Fact]
    public async Task ABadBreakTypeIsReadAsALineBreak()
    {
        var docx = PackageParts.WithPart(Agendas.Fixture("board-table.docx"), "word/document.xml", xml => xml.Replace("<w:r>", "<w:r><w:br w:type=\"sideways\"/>", StringComparison.Ordinal));

        var result = await new DocxAgendaParser().ParseAsync(new MemoryStream(docx), Options("board.docx"), CancellationToken.None);

        Assert.NotEmpty(result.Items);
    }

    [Fact]
    public async Task ABadNumberingReferenceInAWordStyleIsIgnored()
    {
        var docx = PackageParts.WithPart(Agendas.Fixture("messy-mixed.docx"), "word/styles.xml", xml => xml.Replace("<w:pPr>", "<w:pPr><w:numPr><w:ilvl w:val=\"e\"/><w:numId w:val=\"x\"/></w:numPr><w:outlineLvl w:val=\"q\"/>", StringComparison.Ordinal));

        var result = await new DocxAgendaParser().ParseAsync(new MemoryStream(docx), Options("messy.docx"), CancellationToken.None);

        Assert.NotEmpty(result.Items);
    }

    [Theory]
    [InlineData("word/document.xml", "<w:body><w:p>")]
    [InlineData("word/styles.xml", "<w:styles <<")]
    public async Task AWordDocumentWithBrokenXmlIsUnreadable(string part, string damage)
    {
        var docx = PackageParts.WithPart(Agendas.Fixture("board-table.docx"), part, xml => xml.Replace("<w:body>", damage, StringComparison.Ordinal).Replace("<w:styles ", damage, StringComparison.Ordinal));

        var error = await Assert.ThrowsAsync<AgendaImportException>(() => new DocxAgendaParser().ParseAsync(new MemoryStream(docx), Options("board.docx"), CancellationToken.None));

        Assert.Equal(AgendaErrorCodes.Unreadable, error.Code);
        Assert.StartsWith("board.docx looks like a Word document but could not be read", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AWordDocumentWithoutItsMainPartIsUnreadable()
    {
        var entries = PackageParts.Read(Agendas.Fixture("board-table.docx")).Where(e => e.Name != "word/document.xml");

        var error = await Assert.ThrowsAsync<AgendaImportException>(() => new DocxAgendaParser().ParseAsync(new MemoryStream(PackageParts.Write(entries)), Options("board.docx"), CancellationToken.None));

        Assert.Equal(AgendaErrorCodes.Unreadable, error.Code);
    }

    [Theory]
    [InlineData(" t=\"inlineStr\"", " t=\"zz\"")]
    [InlineData("<x:row r=\"2\"", "<x:row r=\"x\"")]
    [InlineData("<x:c r=\"A3\"", "<x:c r=\"A3\" s=\"x\"")]
    public async Task ABadCellValueInAWorkbookIsIgnored(string from, string to)
    {
        var xlsx = PackageParts.WithPart(Agendas.Fixture("simple-list.xlsx"), "xl/worksheets/sheet1.xml", xml => xml.Replace(from, to, StringComparison.Ordinal));

        var result = await new XlsxAgendaParser().ParseAsync(new MemoryStream(xlsx), Options("list.xlsx"), CancellationToken.None);

        Assert.NotEmpty(result.Items);
    }

    [Fact]
    public async Task ASheetPointingAtAMissingPartIsSkipped()
    {
        var xlsx = PackageParts.WithPart(Agendas.Fixture("simple-list.xlsx"), "xl/workbook.xml", xml => xml.Replace("r:id=\"", "r:id=\"missing", StringComparison.Ordinal));

        var error = await Assert.ThrowsAsync<AgendaImportException>(() => new XlsxAgendaParser().ParseAsync(new MemoryStream(xlsx), Options("list.xlsx"), CancellationToken.None));

        Assert.Equal(AgendaErrorCodes.NoItems, error.Code);
    }

    [Fact]
    public async Task AWorkbookWithBrokenXmlIsUnreadable()
    {
        var xlsx = PackageParts.WithPart(Agendas.Fixture("simple-list.xlsx"), "xl/worksheets/sheet1.xml", xml => xml[..(xml.Length / 2)]);

        var error = await Assert.ThrowsAsync<AgendaImportException>(() => new XlsxAgendaParser().ParseAsync(new MemoryStream(xlsx), Options("list.xlsx"), CancellationToken.None));

        Assert.Equal(AgendaErrorCodes.Unreadable, error.Code);
        Assert.StartsWith("list.xlsx looks like an Excel workbook but could not be read", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("၈:၃၀ Budget")]
    [InlineData("၁. Budget")]
    [InlineData("1.၂ Budget")]
    [InlineData("Budget ...... ၁၀:၀၀")]
    public async Task DigitsFromOtherScriptsAreTextNotMarkers(string line)
    {
        var text = $"1. Welcome\n{line}\n2. Close";

        var pasted = await Agendas.PasteAsync(text);
        var file = await Agendas.ImportAsync(Encoding.UTF8.GetBytes(text), "agenda.txt");

        Assert.Contains(pasted.Items, i => i.Text.Contains("Budget", StringComparison.Ordinal));
        Assert.Contains(file.Items, i => i.Text.Contains("Budget", StringComparison.Ordinal));
    }

    private static AgendaParseOptions Options(string name) => new() { FileName = name };
}
