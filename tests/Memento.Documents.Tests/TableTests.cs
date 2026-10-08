using Memento.Documents.Agenda;
using Memento.Documents.Agenda.Tables;
using Memento.Documents.Tests.Support;

namespace Memento.Documents.Tests;

public sealed class TableTests
{
    [Fact]
    public void ReadsQuotedFieldsDoubledQuotesAndLineBreaksInsideQuotes()
    {
        var rows = DelimitedReader.Read("a,\"b, c\",\"say \"\"hi\"\"\"\r\n\"two\nlines\",x,\r\n", ',', CancellationToken.None, out _);

        Assert.Equal(2, rows.Count);
        Assert.Equal(["a", "b, c", "say \"hi\""], rows[0]);
        Assert.Equal(["two\nlines", "x", string.Empty], rows[1]);
    }

    [Theory]
    [InlineData("Item;Owner\nWelcome;Chair\nBudget;Treasurer", ';')]
    [InlineData("Item,Owner\nWelcome,Chair", ',')]
    [InlineData("Item\tOwner\nWelcome\tChair", '\t')]
    public void DetectsTheDelimiter(string text, char delimiter) =>
        Assert.Equal(delimiter, DelimitedReader.DetectDelimiter(text, preferTab: false));

    [Fact]
    public async Task WithoutAHeaderTheFirstTextColumnIsUsedAndMarkedWhenAnotherCouldBeIt()
    {
        var result = await Agendas.ImportTextAsync("Welcome,Chair opens the meeting\nBudget review,Treasurer walks through it\n", "agenda.csv");

        Assert.Equal(["0|Welcome?", "0|Budget review?"], Agendas.Shape(result));
        Assert.Equal(UncertainReasons.ColumnGuessed("column A"), result.Items[0].UncertainReason);
    }

    [Fact]
    public async Task ASingleColumnWithoutAHeaderIsNotMarked()
    {
        var result = await Agendas.ImportTextAsync("Welcome\nBudget review\nClose\n", "agenda.csv");

        Assert.Equal(["0|Welcome", "0|Budget review", "0|Close"], Agendas.Shape(result));
    }

    [Fact]
    public async Task CellsCopiedFromASpreadsheetArePastedAsATable()
    {
        var result = await Agendas.PasteAsync("Time\tTopic\tLead\n09:00\tWelcome\tChair\n09:15\tBudget\tTreasurer\n");

        Assert.Equal(AgendaSourceKind.PastedText, result.Source);
        Assert.Equal(["0|Welcome", "0|Budget"], Agendas.Shape(result));
        Assert.Equal("09:15", result.Items[1].Time);
    }

    [Fact]
    public async Task AFirstColumnThatCountsUpIsTheNumbering()
    {
        var result = await Agendas.ImportTextAsync("1,Welcome\n2,Budget\n4,Close\n", "agenda.csv");

        Assert.Equal(["0|Welcome", "0|Budget", "0|Close?"], Agendas.Shape(result));
        Assert.Equal("4", result.Items[2].Number);
    }

    [Fact]
    public async Task ASectionLabelLeftOfTheAgendaColumnBecomesAHeading()
    {
        var result = await Agendas.ImportTextAsync("Part,Topic\nMorning,\n,Welcome\n,Budget\nAfternoon,\n,Roadmap\n", "agenda.csv");

        Assert.Equal(["0|Morning", "1|Welcome", "1|Budget", "0|Afternoon", "1|Roadmap"], Agendas.Shape(result));
    }

    [Fact]
    public void ColumnLettersFollowSpreadsheets()
    {
        Assert.Equal("A", AgendaTableReader.ColumnLetter(0));
        Assert.Equal("Z", AgendaTableReader.ColumnLetter(25));
        Assert.Equal("AA", AgendaTableReader.ColumnLetter(26));
    }

    [Fact]
    public async Task SheetItemsPointAtTheirCell()
    {
        var result = await Agendas.ImportAsync(Agendas.Fixture("planning-second-sheet.xlsx"), "planning.xlsx");

        Assert.Equal("Agenda", result.Items[0].Location.Sheet);
        Assert.Equal("B4", result.Items[0].Location.Cell);
        Assert.Equal("sheet Agenda, cell B4", result.Items[0].Location.ToString());
    }
}
