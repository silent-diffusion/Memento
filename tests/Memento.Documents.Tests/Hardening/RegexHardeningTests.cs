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
/// <remarks>
/// These tests measure work, never time (<see cref="Work"/>). A stopwatch failed on a loaded machine (security audit 2,
/// SA2-70), and so did counting the matches that ran past the 250 ms timeout: the timeout is wall-clock time, so a
/// runner that stalls mid-match cuts a linear match short. The hostile text now runs without the timeout and the
/// processor cycles it used must fit in the timeout (or the parse ceiling) on a 1 GHz processor. The regressions these
/// guard against (cubic on long leaders, quadratic on markers) cost hundreds of times that.
/// </remarks>
public sealed class RegexHardeningTests
{
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
            Assert.Same(regex, RegexGuard.Use(regex));
        }
    }

    [Fact]
    public void AMatchThatRunsPastItsTimeoutCountsAsNoMatchAndIsTallied()
    {
        // Exponential on purpose, with a 1 ms limit: a stall can only make the timeout come sooner.
        var catastrophic = new Regex("^(a+)+$", RegexOptions.None, TimeSpan.FromMilliseconds(1));
        var tally = RegexGuard.StartTally(untimed: false);

        Assert.False(RegexGuard.IsMatch(catastrophic, new string('a', 40) + "!"));
        Assert.Equal(1, tally.Timeouts);
    }

    [Theory]
    [InlineData(10_000_000L, 1, true)]
    [InlineData(10_000_000L, RegexGuard.MaxAttempts - 1, true)]
    [InlineData(10_000_000L, RegexGuard.MaxAttempts, false)]
    [InlineData(249_000_000L, 1, true)]
    [InlineData(250_000_000L, 1, false)]
    [InlineData(4_000_000_000L, 1, false)]
    public void AMatchStoppedWhileWaitingToRunRunsAgainButAMatchThatWorkedItsTimeoutDoesNot(long cyclesSpent, int attempts, bool again)
    {
        // A loaded PC stalled a 250 ms match on "**bold**" and the line kept its markup; a stall is told from work by the
        // processor cycles the thread used: under what a 1 GHz processor does in the timeout means it mostly waited.
        Assert.Equal(again, RegexGuard.StalledRatherThanSlow(TimeSpan.FromMilliseconds(RegexGuard.TimeoutMilliseconds), cyclesSpent, attempts));
    }

    [Theory]
    [InlineData(' ')]
    [InlineData('\t')]
    [InlineData('.')]
    [InlineData('…')]
    public void ALongLeaderWithNoTimeAfterItIsQuick(char leader)
    {
        var text = "Welcome" + new string(leader, 3_900) + "x9";
        var found = true;

        var cycles = Work.Of(() => found = MarkerParser.TryParseTrailingTime(text, out _, out _));

        Assert.False(found);
        Assert.InRange(cycles, 1, Work.MatchBudget);
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

        await Quickly(() => Agendas.PasteAsync(text, Work.Unlimited));
    }

    [Fact]
    public async Task ManyHeadingsPaddedWithSpacesAreQuick()
    {
        var text = string.Concat(Enumerable.Repeat("# a" + new string(' ', 3_900) + "b\n", 2_000)) + "- [ ] item\n";

        await Quickly(() => Agendas.ImportAsync(Encoding.UTF8.GetBytes(text), "agenda.md", options: Work.Unlimited));
    }

    [Theory]
    [InlineData("*a ", 1_300)]
    [InlineData("[a](", 900)]
    [InlineData("<a ", 1_300)]
    [InlineData("`", 3_900)]
    public void EmphasisMarkersInALongLineAreQuick(string marker, int count)
    {
        var line = string.Concat(Enumerable.Repeat(marker, count));

        var cycles = Work.Of(() => MarkdownInline.Strip(line));

        Assert.InRange(cycles, 1, Work.MatchBudget);
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
        var looksLikeMarkdown = true;

        var cycles = Work.Of(() => looksLikeMarkdown = MarkdownAgendaParser.LooksLikeMarkdown(text));

        Assert.False(looksLikeMarkdown);
        Assert.InRange(cycles, 1, 2_000 * Work.CyclesPerMillisecond);
    }

    /// <summary>
    /// The parse (a refusal is fine) does no more work than the product's parse ceiling allows on a slow processor, and
    /// no single match more than its timeout allows.
    /// </summary>
    private static async Task Quickly(Func<Task> parse)
    {
        var (cycles, longestMatch) = await Work.OfParseAsync(parse);

        Assert.InRange(cycles, 1, Work.ParseBudget);
        Assert.InRange(longestMatch, 1, Work.MatchBudget);
    }
}
