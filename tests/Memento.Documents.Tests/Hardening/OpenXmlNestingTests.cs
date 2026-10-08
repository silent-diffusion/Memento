using System.Text;
using Memento.Documents.Agenda;
using Memento.Documents.Agenda.OpenXml;
using Memento.Documents.Tests.Support;

namespace Memento.Documents.Tests.Hardening;

/// <summary>
/// XML parts nested thousands deep would overflow the host's stack while the SDK builds its tree (it recurses per
/// level), so they are refused by a streaming scan first; a DTD is refused too. The inputs are generated here.
/// </summary>
public sealed class OpenXmlNestingTests
{
    [Theory]
    [InlineData("<w:sdt><w:sdtContent>", "</w:sdtContent></w:sdt>")]
    [InlineData("<w:customXml w:element=\"x\">", "</w:customXml>")]
    [InlineData("<w:tbl><w:tr><w:tc>", "</w:tc></w:tr></w:tbl>")]
    public async Task AWordDocumentNestedThousandsDeepIsRefusedWithoutACrash(string open, string close)
    {
        var docx = PackageParts.WithNestedPart(Agendas.Fixture("board-table.docx"), "word/document.xml", open, close, 20_000);

        var error = await Assert.ThrowsAsync<AgendaImportException>(() => Docx(docx));

        Assert.Equal(AgendaErrorCodes.Unreadable, error.Code);
        Assert.Equal(
            "board.docx looks like a Word document but has elements nested more than 256 deep, which Word and Excel never write, so it was not read. Nothing was imported. Open it in the app that made it and save it again, or paste the items as text.",
            error.Message);
    }

    [Theory]
    [InlineData("xl/worksheets/sheet1.xml", "<x:a xmlns:x=\"urn:x\">", "</x:a>")]
    [InlineData("xl/workbook.xml", "<x:a xmlns:x=\"urn:x\">", "</x:a>")]
    public async Task AWorkbookNestedThousandsDeepIsRefusedWithoutACrash(string part, string open, string close)
    {
        var xlsx = PackageParts.WithNestedPart(Agendas.Fixture("simple-list.xlsx"), part, open, close, 20_000);

        var error = await Assert.ThrowsAsync<AgendaImportException>(() => new XlsxAgendaParser().ParseAsync(new MemoryStream(xlsx), new AgendaParseOptions { FileName = "list.xlsx" }, CancellationToken.None));

        Assert.Equal(AgendaErrorCodes.Unreadable, error.Code);
        Assert.Contains("nested more than 256 deep", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APartWithADtdIsRefused()
    {
        var docx = PackageParts.WithPart(
            Agendas.Fixture("board-table.docx"),
            "word/document.xml",
            xml => xml.Replace("?>", "?><!DOCTYPE w:document [<!ENTITY a \"aaaaaaaaaa\"><!ENTITY b \"&a;&a;&a;&a;&a;&a;&a;&a;\">]>", StringComparison.Ordinal));

        var error = await Assert.ThrowsAsync<AgendaImportException>(() => Docx(docx));

        Assert.Equal(AgendaErrorCodes.Unreadable, error.Code);
        Assert.Contains("contains a document type definition (DTD)", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ContentControlsNestedPastTheReadingDepthAreRefused()
    {
        // 100 levels of content controls is 200 XML levels: under the scan's limit, over the reader's.
        var docx = WithNestedControls(100);

        var error = await Assert.ThrowsAsync<AgendaImportException>(() => Docx(docx));

        Assert.Equal(AgendaErrorCodes.Unreadable, error.Code);
    }

    [Fact]
    public async Task ContentControlsNestedAFewDeepAreStillRead()
    {
        var result = await Docx(WithNestedControls(10));

        Assert.Contains(result.Items, i => i.Text == "Nested item");
    }

    private static byte[] WithNestedControls(int depth)
    {
        var content = new StringBuilder();
        for (var d = 0; d < depth; d++)
        {
            content.Append("<w:sdt><w:sdtContent>");
        }

        content.Append("<w:p><w:r><w:t>1. Nested item</w:t></w:r></w:p><w:p><w:r><w:t>2. Second nested item</w:t></w:r></w:p>");
        for (var d = 0; d < depth; d++)
        {
            content.Append("</w:sdtContent></w:sdt>");
        }

        return PackageParts.WithPart(Agendas.Fixture("board-table.docx"), "word/document.xml", xml => xml.Replace("<w:body>", "<w:body>" + content, StringComparison.Ordinal));
    }

    private static Task<AgendaParseResult> Docx(byte[] docx) =>
        new DocxAgendaParser().ParseAsync(new MemoryStream(docx), new AgendaParseOptions { FileName = "board.docx" }, CancellationToken.None);
}
