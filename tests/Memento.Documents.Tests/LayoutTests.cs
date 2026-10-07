using Memento.Documents.Agenda.Layout;

namespace Memento.Documents.Tests;

/// <summary>Lines from word boxes, and reading order on two-column pages.</summary>
public sealed class LayoutTests
{
    [Fact]
    public void WordsThatOverlapVerticallyShareALineInLeftToRightOrder()
    {
        WordBox[] words =
        [
            new("review", 120, 102, 60, 20),
            new("Budget", 40, 100, 70, 20),
            new("Close", 40, 140, 50, 20),
        ];

        var lines = LineBuilder.Build(words);

        Assert.Equal(["Budget review", "Close"], lines.Select(l => l.Text).ToArray());
    }

    [Fact]
    public void ASlightlySkewedLineStaysOneLine()
    {
        var words = Enumerable.Range(0, 8).Select(i => new WordBox($"w{i}", 40 + (i * 70), 100 + (i * 3), 60, 24)).ToList();

        var lines = LineBuilder.Build(words);

        Assert.Single(lines);
    }

    [Fact]
    public void TwoColumnsAreReadLeftThenRight()
    {
        var words = new List<WordBox> { new("Agenda", 260, 20, 80, 20) };
        string[] left = ["Left one item", "Left two item", "Left three item", "Left four item"];
        string[] right = ["Right one item", "Right two item", "Right three item", "Right four item"];
        for (var i = 0; i < left.Length; i++)
        {
            words.AddRange(Words(left[i], 40, 80 + (i * 30)));
            words.AddRange(Words(right[i], 360, 80 + (i * 30)));
        }

        var (lines, twoColumns) = PageLayout.ReadingOrder(words);

        Assert.True(twoColumns);
        Assert.Equal(
            ["Agenda", .. left, .. right],
            lines.Select(l => l.Line.Text).ToArray());
    }

    [Fact]
    public void AShortRightColumnOnTheSameBaselinesIsATableNotAColumn()
    {
        var words = new List<WordBox>();
        string[] topics = ["Welcome and goals", "Budget for the year", "Roadmap and hiring", "Close and next steps"];
        string[] people = ["Chair", "Treasurer", "Lead", "Chair"];
        for (var i = 0; i < topics.Length; i++)
        {
            words.AddRange(Words(topics[i], 40, 80 + (i * 30)));
            words.AddRange(Words(people[i], 420, 80 + (i * 30)));
        }

        var (lines, twoColumns) = PageLayout.ReadingOrder(words);

        Assert.False(twoColumns);
        Assert.Equal("Welcome and goals Chair", lines[0].Line.Text);
    }

    private static IEnumerable<WordBox> Words(string text, double left, double top)
    {
        var x = left;
        foreach (var word in text.Split(' '))
        {
            var width = word.Length * 9;
            yield return new WordBox(word, x, top, width, 18);
            x += width + 6;
        }
    }
}
