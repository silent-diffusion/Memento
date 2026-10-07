using Memento.Documents.Agenda;
using Memento.Documents.Tests.Support;

namespace Memento.Documents.Tests;

/// <summary>The shared rules every format goes through: levels, sections, numbering checks and uncertainty reasons.</summary>
public sealed class StructureTests
{
    [Fact]
    public async Task ARepeatedNumberMarksTheSecondItem()
    {
        var result = await Agendas.PasteAsync("1. Welcome\n2. Budget\n2. Roadmap\n3. Close");

        Assert.Equal(["0|Welcome", "0|Budget", "0|Roadmap?", "0|Close"], Agendas.Shape(result));
        Assert.Equal(UncertainReasons.NumberingRepeats("2"), result.Items[2].UncertainReason);
    }

    [Fact]
    public async Task ASkippedNumberMarksTheItemAfterTheGap()
    {
        var result = await Agendas.PasteAsync("1. Welcome\n2. Budget\n4. Roadmap");

        Assert.Equal(UncertainReasons.NumberingSkips("2", "4"), result.Items[2].UncertainReason);
    }

    [Fact]
    public async Task NumberingThatGoesBackOrStartsLateIsMarked()
    {
        var back = await Agendas.PasteAsync("1. Welcome\n2. Budget\n3. Roadmap\n2. Close");
        var late = await Agendas.PasteAsync("3. Welcome\n4. Budget");

        Assert.Equal(UncertainReasons.NumberingGoesBack("3", "2"), back.Items[3].UncertainReason);
        Assert.Equal(UncertainReasons.NumberingStartsLate("3"), late.Items[0].UncertainReason);
        Assert.False(late.Items[1].Uncertain);
    }

    [Fact]
    public async Task MarkdownStyleRepeatedOnesAreNotAnError()
    {
        var result = await Agendas.PasteAsync("1. Welcome\n1. Budget\n1. Close");

        Assert.Equal(0, result.UncertainCount);
    }

    [Fact]
    public async Task NumberingCarriesOnAcrossSections()
    {
        var result = await Agendas.PasteAsync("Morning:\n1. Welcome\n2. Budget\nAfternoon:\n3. Roadmap\n4. Close");

        Assert.Equal(["0|Morning", "1|Welcome", "1|Budget", "0|Afternoon", "1|Roadmap", "1|Close"], Agendas.Shape(result));
    }

    [Theory]
    [InlineData("Finance: - Budget review")]
    [InlineData("Opening • Welcome from the chair")]
    public async Task AHeadingRunIntoItsFirstBulletIsMarked(string line)
    {
        var result = await Agendas.PasteAsync($"1. Introductions\n2. {line}\n3. Close");

        Assert.Equal(UncertainReasons.HeadingMerged, result.Items[1].UncertainReason);
    }

    [Fact]
    public async Task SeveralItemsOnOneLineAreMarked()
    {
        var result = await Agendas.PasteAsync("Welcome • Budget • Roadmap\nClose");

        Assert.Equal(UncertainReasons.SeveralItemsMerged, result.Items[0].UncertainReason);
    }

    [Fact]
    public async Task TwoNumberedItemsOnOneLineAreMarked()
    {
        var result = await Agendas.PasteAsync("1. Welcome 2. Budget\n3. Close");

        Assert.Equal(UncertainReasons.NumberedItemsMerged("2"), result.Items[0].UncertainReason);
    }

    [Fact]
    public async Task VeryShortAndVeryLongItemsAreMarked()
    {
        var longText = string.Join(' ', Enumerable.Repeat("discussion", 21));
        var result = await Agendas.PasteAsync($"1. Welcome\n2. x\n3. {longText}\n4. Q&A");

        Assert.Equal(UncertainReasons.TooShort, result.Items[1].UncertainReason);
        Assert.Equal(UncertainReasons.TooLong(longText.Length), result.Items[2].UncertainReason);
        Assert.False(result.Items[3].Uncertain);
    }

    [Fact]
    public async Task PlainLinesUnderColonHeadingsBecomeSubItems()
    {
        var result = await Agendas.PasteAsync("Morning:\nWelcome\nBudget\n\nAfternoon:\nRoadmap");

        Assert.Equal(["0|Morning", "1|Welcome", "1|Budget", "0|Afternoon", "1|Roadmap"], Agendas.Shape(result));
    }

    [Fact]
    public async Task APlainLineAboveBulletsIsASection()
    {
        var result = await Agendas.PasteAsync("Morning\n- Welcome\n- Budget\nAfternoon\n- Roadmap");

        Assert.Equal(["0|Morning", "1|Welcome", "1|Budget", "0|Afternoon", "1|Roadmap"], Agendas.Shape(result));
    }

    [Fact]
    public async Task APlainLineInsideACountingListIsASibling()
    {
        var result = await Agendas.PasteAsync("1. Welcome\n2. Budget\nLunch\n3. Roadmap");

        Assert.Equal(["0|Welcome", "0|Budget", "0|Lunch", "0|Roadmap"], Agendas.Shape(result));
    }

    [Fact]
    public async Task WrappedLinesJoinTheirItem()
    {
        var result = await Agendas.PasteAsync("1. Customer feedback from the\n   spring survey\n2. Close");

        Assert.Equal(["0|Customer feedback from the spring survey", "0|Close"], Agendas.Shape(result));
    }

    [Fact]
    public async Task LettersAfterHAreLettersAndRomanNumeralsStayRoman()
    {
        var letters = await Agendas.PasteAsync("1. Topics\n" + string.Join('\n', "abcdefghij".Select(c => $"  {c}. Topic {c}")));
        var roman = await Agendas.PasteAsync("I. Opening\nII. Reports\nIII. Close");

        Assert.Equal(0, letters.UncertainCount);
        Assert.Equal("i", letters.Items[9].Number);
        Assert.All(letters.Items.Skip(1), i => Assert.Equal(1, i.Level));
        Assert.Equal(["0|Opening", "0|Reports", "0|Close"], Agendas.Shape(roman));
        Assert.Equal("III", roman.Items[2].Number);
    }

    [Fact]
    public async Task OutlineNumbersSetTheLevelAndAreChecked()
    {
        var result = await Agendas.PasteAsync("1. Intro\n1.1 Scope\n1.2 Risks\n2. Plan\n2.1 First quarter\n2.3 Third quarter");

        Assert.Equal(
            ["0|Intro", "1|Scope", "1|Risks", "0|Plan", "1|First quarter", "1|Third quarter?"],
            Agendas.Shape(result));
        Assert.Equal(UncertainReasons.NumberingSkips("2.1", "2.3"), result.Items[5].UncertainReason);
        Assert.Equal("2.3", result.Items[5].Number);
    }

    [Fact]
    public async Task MeetingDetailsAreLeftOutButKeptInAWarning()
    {
        var result = await Agendas.PasteAsync("Agenda\nDate: 3 May 2031\nAttendees:\n- A. Example\n- B. Example\n\n1. Welcome\n2. Close");

        Assert.Equal("Agenda", result.Title);
        Assert.Equal(["0|Welcome", "0|Close"], Agendas.Shape(result));
        var warning = Assert.Single(result.Warnings, w => w.Code == AgendaWarningCodes.DetailsSkipped);
        Assert.Contains("Date: 3 May 2031", warning.Content, StringComparison.Ordinal);
        Assert.Contains("A. Example", warning.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MoreThanTheItemLimitGoesIntoAWarning()
    {
        var text = string.Join('\n', Enumerable.Range(1, 205).Select(i => $"{i}. Topic number {i}"));
        var result = await Agendas.PasteAsync(text);

        Assert.Equal(AgendaLimits.MaxItems, result.Items.Count);
        var warning = Assert.Single(result.Warnings, w => w.Code == AgendaWarningCodes.TooManyItems);
        Assert.Equal(5, warning.Content!.Split('\n').Length);
        Assert.StartsWith("Topic number 201", warning.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TextWithNoItemsIsASpecificError()
    {
        var error = await Assert.ThrowsAsync<AgendaImportException>(() => Agendas.PasteAsync("   \n\n  "));

        Assert.Equal(AgendaErrorCodes.NoItems, error.Code);
        Assert.Contains("Nothing was imported", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TimesAreKeptApartFromTheText()
    {
        var result = await Agendas.PasteAsync("10:00 – Welcome\n10:15 – Budget review\nLunch ........ 12:30");

        Assert.Equal(["10:00", "10:15", "12:30"], result.Items.Select(i => i.Time ?? string.Empty).ToArray());
        Assert.Equal(["Welcome", "Budget review", "Lunch"], result.Items.Select(i => i.Text).ToArray());
    }

    [Fact]
    public async Task PastedMarkdownIsReadAsMarkdown()
    {
        var result = await Agendas.PasteAsync("# Sync\n## Updates\n- [ ] **Design** status\n- [x] Hiring");

        Assert.Equal(AgendaSourceKind.PastedText, result.Source);
        Assert.Equal("Sync", result.Title);
        Assert.Equal(["0|Updates", "1|Design status", "1|Hiring"], Agendas.Shape(result));
    }

    [Fact]
    public async Task EveryItemKnowsWhereItCameFrom()
    {
        var result = await Agendas.PasteAsync("Agenda\n\n1. Welcome\n2. Close");

        Assert.Equal(3, result.Items[0].Location.Line);
        Assert.Equal("line 4", result.Items[1].Location.ToString());
        Assert.Null(result.SourceName);
    }
}
