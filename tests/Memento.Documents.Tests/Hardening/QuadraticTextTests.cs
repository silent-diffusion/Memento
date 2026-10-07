using System.Diagnostics;
using System.Text;
using Memento.Documents.Agenda;
using Memento.Documents.Agenda.Tables;
using Memento.Documents.Agenda.Text;
using Memento.Documents.Tests.Support;

namespace Memento.Documents.Tests.Hardening;

/// <summary>Text handling stays linear: quote marks, wrapped lines and wide or long tables cost per character, not per character squared.</summary>
public sealed class QuadraticTextTests
{
    private static readonly TimeSpan Quick = TimeSpan.FromSeconds(5);

    [Theory]
    [InlineData("> > Welcome", "Welcome")]
    [InlineData("   >>Budget", "Budget")]
    [InlineData("Plain", "Plain")]
    [InlineData(">", "")]
    public void QuoteMarksAreRemovedInOneScan(string line, string expected)
    {
        Assert.Equal(expected, TextLines.StripQuote(line));
    }

    [Fact]
    public void AHundredThousandQuoteMarksAreQuick()
    {
        var watch = Stopwatch.StartNew();

        Assert.Equal("x", TextLines.StripQuote(new string('>', 100_000) + "x"));
        Assert.Equal("x", TextLines.StripQuoteMarks(string.Concat(Enumerable.Repeat("> ", 100_000)) + "x"));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1), $"took {watch.Elapsed.TotalMilliseconds:0} ms");
    }

    [Fact]
    public async Task AFloodOfWrappedLinesStopsJoiningPastTheCap()
    {
        // Enough marked items after the wrapped lines that the text reads as a list, so the lines are joined until the cap.
        var text = "1. Start\n" + string.Concat(Enumerable.Repeat("  continued words\n", 3_000)) + string.Concat(Enumerable.Range(2, 3_100).Select(i => $"{i}. Item\n"));
        var watch = Stopwatch.StartNew();

        var result = await Agendas.PasteAsync(text);

        Assert.True(watch.Elapsed < Quick, $"took {watch.Elapsed.TotalSeconds:0.0} s");
        Assert.All(result.Items, i => Assert.True(i.Text.Length < 2_100, $"{i.Text.Length} characters"));
        Assert.Contains(result.Warnings, w => w.Code == AgendaWarningCodes.TooManyItems);
    }

    [Fact]
    public void ADelimitedFileIsReadUpToTheTableLimits()
    {
        var text = string.Join(',', Enumerable.Range(0, 70).Select(i => $"c{i}")) + "\n" + string.Concat(Enumerable.Repeat("x\n", 12_000));

        var rows = DelimitedReader.Read(text, ',', CancellationToken.None, out var truncated);

        Assert.True(truncated);
        Assert.Equal(AgendaTableReader.MaxRows, rows.Count);
        Assert.Equal(AgendaTableReader.MaxColumns, rows[0].Count);
    }

    [Fact]
    public async Task OneRowOfAMillionCommasIsQuickAndSaysWhatWasLeftOut()
    {
        var csv = Encoding.UTF8.GetBytes("Item,Time\nWelcome,9:00\n" + new string(',', 1_000_000) + "\n" + string.Concat(Enumerable.Repeat("Topic,10:00\n", 50_000)));
        var watch = Stopwatch.StartNew();

        var result = await Agendas.ImportAsync(csv, "agenda.csv");

        Assert.True(watch.Elapsed < Quick, $"took {watch.Elapsed.TotalSeconds:0.0} s");
        Assert.Contains(result.Warnings, w => w.Code == AgendaWarningCodes.Unparsed && w.Message.Contains("first 10,000 rows and 64 columns", StringComparison.Ordinal));
    }

    [Fact]
    public void AWideTableFromAnyFormatIsCut()
    {
        var rows = new List<TableRow>
        {
            new(["Item", .. Enumerable.Range(0, 100).Select(i => $"Note {i}")], new AgendaSourceLocation { Row = 1 }),
            new(["Welcome", .. Enumerable.Range(0, 100).Select(i => "x")], new AgendaSourceLocation { Row = 2 }),
        };

        var table = AgendaTableReader.Read(rows, CancellationToken.None);

        Assert.Contains(table.Warnings, w => w.Code == AgendaWarningCodes.Unparsed);
        Assert.Contains(table.Lines, l => l.Text == "Welcome");
    }

    [Fact]
    public void ACellWithManyLinesIsJoinedOnce()
    {
        var cell = string.Join('\n', Enumerable.Repeat("more words", 100_000));
        var rows = new List<TableRow>
        {
            new(["Item"], new AgendaSourceLocation { Row = 1 }),
            new([cell], new AgendaSourceLocation { Row = 2 }),
        };
        var watch = Stopwatch.StartNew();

        var table = AgendaTableReader.Read(rows, CancellationToken.None);

        Assert.True(watch.Elapsed < Quick, $"took {watch.Elapsed.TotalSeconds:0.0} s");
        Assert.Equal(("more words ".Length * 100_000) - 1, table.Lines.Single(l => l.Text.StartsWith("more", StringComparison.Ordinal)).Text.Length);
    }
}
