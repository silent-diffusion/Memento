using System.Diagnostics;
using Memento.Documents.Agenda;
using Memento.Documents.Agenda.OpenXml;
using Memento.Documents.Tests.Support;

namespace Memento.Documents.Tests.Hardening;

/// <summary>Row and column references past Excel's limits are skipped instead of wrapping or building a huge table.</summary>
public sealed class XlsxBoundsTests
{
    [Theory]
    [InlineData("A1", 0)]
    [InlineData("b7", 1)]
    [InlineData("XFD1", 16_383)]
    [InlineData("XFE1", -1)]
    [InlineData("AAAA1", -1)]
    [InlineData("ZZZZZZ1", -1)]
    [InlineData("ZZZZZZZZZZZZZZZZZZ1", -1)]
    public void ColumnLettersStopAtExcelsLastColumn(string reference, int expected)
    {
        Assert.Equal(expected, XlsxAgendaParser.ColumnIndex(reference));
    }

    [Theory]
    [InlineData("4294967295")]
    [InlineData("2147483648")]
    [InlineData("0")]
    [InlineData("10001")]
    public async Task ARowPastTheLimitIsSkipped(string rowNumber)
    {
        var xlsx = WithExtraRow($"<x:row r=\"{rowNumber}\"><x:c r=\"A{rowNumber}\" t=\"inlineStr\"><x:is><x:t>Stray</x:t></x:is></x:c></x:row>");

        var result = await Parse(xlsx);

        Assert.DoesNotContain(result.Items, i => i.Text == "Stray");
        Assert.Contains(result.Items, i => i.Text == "Introductions");
    }

    [Theory]
    [InlineData("ZZZZZZ8")]
    [InlineData("XFD8")]
    [InlineData("BM8")]
    public async Task ACellPastTheReadColumnsIsSkipped(string reference)
    {
        var xlsx = WithExtraRow($"<x:row r=\"8\"><x:c r=\"{reference}\" t=\"inlineStr\"><x:is><x:t>Far away</x:t></x:is></x:c></x:row>");

        var result = await Parse(xlsx);

        Assert.DoesNotContain(result.Items, i => i.Text == "Far away");
    }

    [Fact]
    public async Task CellsAtOppositeCornersDoNotBuildAHugeTable()
    {
        var xlsx = WithExtraRow(
            "<x:row r=\"9\"><x:c r=\"XFD9\" t=\"inlineStr\"><x:is><x:t>Corner</x:t></x:is></x:c></x:row>" +
            "<x:row r=\"10000\"><x:c r=\"A10000\" t=\"inlineStr\"><x:is><x:t>Bottom</x:t></x:is></x:c><x:c r=\"BL10000\" t=\"inlineStr\"><x:is><x:t>Edge</x:t></x:is></x:c></x:row>");
        var watch = Stopwatch.StartNew();

        var result = await Parse(xlsx);

        Assert.True(watch.Elapsed < WallClock.Limit(5), $"took {watch.Elapsed.TotalSeconds:0.0} s");
        Assert.Contains(result.Items, i => i.Text == "Bottom");
    }

    private static byte[] WithExtraRow(string row) =>
        PackageParts.WithPart(Agendas.Fixture("simple-list.xlsx"), "xl/worksheets/sheet1.xml", xml => xml.Replace("</x:sheetData>", row + "</x:sheetData>", StringComparison.Ordinal));

    private static Task<AgendaParseResult> Parse(byte[] xlsx) =>
        new XlsxAgendaParser().ParseAsync(new MemoryStream(xlsx), new AgendaParseOptions { FileName = "list.xlsx" }, CancellationToken.None);
}
