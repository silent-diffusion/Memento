using Memento.Documents.Agenda.Text;

namespace Memento.Documents.Tests;

public sealed class MarkerParserTests
{
    [Theory]
    [InlineData("1. Welcome", "Decimal", 1, "Welcome")]
    [InlineData("(2) Budget", "Decimal", 2, "Budget")]
    [InlineData("3) Roadmap", "Decimal", 3, "Roadmap")]
    [InlineData("4: Hiring", "Decimal", 4, "Hiring")]
    [InlineData("5 - Close", "Decimal", 5, "Close")]
    [InlineData("#6 Extra", "Decimal", 6, "Extra")]
    [InlineData("1.Welcome", "Decimal", 1, "Welcome")]
    [InlineData("2.3 Risks", "Outline", 3, "Risks")]
    [InlineData("2.10 Review", "Outline", 10, "Review")]
    [InlineData("a) Scope", "LowerLetter", 1, "Scope")]
    [InlineData("B. Plan", "UpperLetter", 2, "Plan")]
    [InlineData("iv. Topic", "LowerRoman", 4, "Topic")]
    [InlineData("XII. Topic", "UpperRoman", 12, "Topic")]
    public void ReadsNumbersLettersAndRomanNumerals(string text, string style, int value, string rest)
    {
        Assert.True(MarkerParser.TryParseMarker(text, out var marker, out var after));
        Assert.Equal(Enum.Parse<MarkerStyle>(style), marker.Style);
        Assert.Equal(value, marker.Value);
        Assert.Equal(rest, after);
    }

    [Theory]
    [InlineData("- Recap", 0)]
    [InlineData("* Recap", 0)]
    [InlineData("• Recap", 0)]
    [InlineData("◦ Recap", 1)]
    [InlineData("– Recap", 1)]
    [InlineData("▪ Recap", 2)]
    [InlineData("\tRecap", 0)]
    public void ReadsBulletsAndTheirFamily(string text, int family)
    {
        Assert.True(MarkerParser.TryParseMarker(text, out var marker, out var rest));
        Assert.Equal(MarkerStyle.Bullet, marker.Style);
        Assert.Equal(family, marker.BulletFamily);
        Assert.Equal("Recap", rest.Trim());
    }

    [Theory]
    [InlineData("- [ ] Hiring plan")]
    [InlineData("- [x] Hiring plan")]
    [InlineData("[ ] Hiring plan")]
    [InlineData("☐ Hiring plan")]
    public void ReadsCheckboxes(string text)
    {
        Assert.True(MarkerParser.TryParseMarker(text, out var marker, out var rest));
        Assert.Equal(MarkerStyle.Checkbox, marker.Style);
        Assert.Equal("Hiring plan", rest);
    }

    [Theory]
    [InlineData("10:00 – Welcome", "10:00", "Welcome")]
    [InlineData("10:00-10:15 Welcome", "10:00-10:15", "Welcome")]
    [InlineData("9:30 am | Welcome", "9:30 am", "Welcome")]
    [InlineData("2 pm: Lunch", "2 pm", "Lunch")]
    [InlineData("10.30 Coffee", "10.30", "Coffee")]
    [InlineData("09.05 Coffee", "09.05", "Coffee")]
    [InlineData("14h30 Atelier", "14h30", "Atelier")]
    public void ReadsTimePrefixes(string text, string time, string rest)
    {
        Assert.False(MarkerParser.TryParseMarker(text, out _, out _));
        Assert.True(MarkerParser.TryParseTimePrefix(text, out var found, out var after));
        Assert.Equal(time, found);
        Assert.Equal(rest, after);
    }

    [Theory]
    [InlineData("9 Welcome")]
    [InlineData("1. Welcome")]
    [InlineData("2.10 Review")]
    [InlineData("25:00 Nonsense")]
    [InlineData("2031 budget")]
    public void DoesNotMistakeNumbersForTimes(string text) =>
        Assert.False(MarkerParser.TryParseTimePrefix(text, out _, out _));

    [Fact]
    public void ReadsATrailingTimeAfterDotLeaders()
    {
        Assert.True(MarkerParser.TryParseTrailingTime("Welcome ........ 10:00", out var time, out var rest));
        Assert.Equal("10:00", time);
        Assert.Equal("Welcome", rest);
    }

    [Theory]
    [InlineData("13:30", true)]
    [InlineData("9:00 - 9:30", true)]
    [InlineData("5 min", false)]
    [InlineData("Lunch", false)]
    public void RecognizesTimeCells(string cell, bool isTime) => Assert.Equal(isTime, MarkerParser.IsTime(cell));

    [Fact]
    public void LeavesPlainTextAlone()
    {
        Assert.False(MarkerParser.TryParseMarker("Welcome and introductions", out _, out _));
        Assert.False(MarkerParser.TryParseMarker("-5 degrees outside", out _, out _));
        Assert.False(MarkerParser.TryParseMarker("*Important* note", out _, out _));
    }
}
