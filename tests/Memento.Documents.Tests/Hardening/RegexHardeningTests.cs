using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Memento.Documents.Agenda;
using Memento.Documents.Agenda.Text;
using Memento.Documents.Tests.Support;

namespace Memento.Documents.Tests.Hardening;

/// <summary>
/// The agenda patterns stay linear on hostile text (long runs of spaces, dots, quote marks or emphasis markers), and
/// every one carries a match timeout as the last line of defence.
/// </summary>
public sealed class RegexHardeningTests
{
    private static readonly TimeSpan Quick = WallClock.Limit(5);

    [Fact]
    public void EveryAgendaPatternHasAMatchTimeout()
    {
        var patterns = typeof(AgendaImporter).Assembly.GetTypes()
            .Where(t => t.Namespace?.StartsWith("Memento.Documents.Agenda", StringComparison.Ordinal) == true)
            .SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
            .Where(m => m.ReturnType == typeof(Regex) && m.GetParameters().Length == 0)
            .ToList();

        Assert.True(patterns.Count >= 40, $"found {patterns.Count} patterns");
        foreach (var pattern in patterns)
        {
            var regex = (Regex)pattern.Invoke(null, null)!;
            Assert.True(regex.MatchTimeout == TimeSpan.FromMilliseconds(RegexGuard.TimeoutMilliseconds), $"{pattern.DeclaringType!.Name}.{pattern.Name} has no match timeout");
        }
    }

    [Theory]
    [InlineData(' ')]
    [InlineData('\t')]
    [InlineData('.')]
    [InlineData('…')]
    public void ALongLeaderWithNoTimeAfterItIsQuick(char leader)
    {
        var text = "Welcome" + new string(leader, 3_900) + "x9";
        var watch = Stopwatch.StartNew();

        Assert.False(MarkerParser.TryParseTrailingTime(text, out _, out _));
        Assert.True(watch.Elapsed < WallClock.Limit(1), $"took {watch.Elapsed.TotalMilliseconds:0} ms");
    }

    [Theory]
    [InlineData("Welcome ........ 10:00", "Welcome", "10:00")]
    [InlineData("Item 3. ...... 9:30-10:15", "Item 3.", "9:30-10:15")]
    [InlineData("Lunch\t\t12:30", "Lunch", "12:30")]
    [InlineData("Close     4 pm", "Close", "4 pm")]
    [InlineData("Break …… 11.00", "Break", "11.00")]
    public void TrailingTimesAreStillFound(string line, string text, string time)
    {
        Assert.True(MarkerParser.TryParseTrailingTime(line, out var parsedTime, out var rest));
        Assert.Equal((text, time), (rest, parsedTime));
    }

    [Fact]
    public async Task ManyLinesOfDotLeadersAreQuick()
    {
        var text = string.Concat(Enumerable.Repeat("1. Item " + new string('.', 3_900) + " x9\n", 1_000));

        await Quickly(() => Agendas.PasteAsync(text));
    }

    [Fact]
    public async Task ManyHeadingsPaddedWithSpacesAreQuick()
    {
        var text = string.Concat(Enumerable.Repeat("# a" + new string(' ', 3_900) + "b\n", 2_000)) + "- [ ] item\n";

        await Quickly(() => Agendas.ImportAsync(Encoding.UTF8.GetBytes(text), "agenda.md"));
    }

    [Fact]
    public void EmphasisMarkersInALongLineAreQuick()
    {
        var watch = Stopwatch.StartNew();

        MarkdownInline.Strip(string.Concat(Enumerable.Repeat("*a ", 1_300)));
        MarkdownInline.Strip(string.Concat(Enumerable.Repeat("[a](", 900)));
        MarkdownInline.Strip(string.Concat(Enumerable.Repeat("<a ", 1_300)));
        MarkdownInline.Strip(new string('`', 3_900));

        Assert.True(watch.Elapsed < WallClock.Limit(1), $"took {watch.Elapsed.TotalMilliseconds:0} ms");
    }

    [Theory]
    [InlineData("**bold** and *italic* and `code`", "bold and italic and code")]
    [InlineData("[the plan](https://example.invalid) ~~old~~", "the plan old")]
    [InlineData("a <b>tag</b> &amp; \\# not a heading", "a tag & # not a heading")]
    public void InlineMarkupIsStillStripped(string markdown, string text)
    {
        Assert.Equal(text, MarkdownInline.Strip(markdown));
    }

    [Theory]
    [InlineData("## Budget ##", "Budget")]
    [InlineData("# Title #   ", "Title")]
    [InlineData("# C#", "C#")]
    [InlineData("### A ## ##", "A ##")]
    public async Task HeadingClosingHashesAreRemoved(string heading, string expected)
    {
        var result = await Agendas.ImportAsync(Encoding.UTF8.GetBytes($"{heading}\n\n- First\n- Second"), "agenda.md");

        Assert.True(result.Title == expected || result.Items.Any(i => i.Text == expected), $"title {result.Title}");
    }

    [Fact]
    public void ManyBlankLinesDoNotLookLikeMarkdownSlowly()
    {
        var text = new string('\n', 1_000_000) + "x";
        var watch = Stopwatch.StartNew();

        Assert.False(MarkdownAgendaParser.LooksLikeMarkdown(text));
        Assert.True(watch.Elapsed < WallClock.Limit(2), $"took {watch.Elapsed.TotalMilliseconds:0} ms");
    }

    private static async Task Quickly(Func<Task> parse)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            await parse();
        }
        catch (AgendaImportException)
        {
            // Refusing is fine; being slow is not.
        }

        Assert.True(watch.Elapsed < Quick, $"took {watch.Elapsed.TotalSeconds:0.0} s");
    }
}
