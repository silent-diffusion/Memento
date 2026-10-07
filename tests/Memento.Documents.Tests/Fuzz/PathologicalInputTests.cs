using System.Diagnostics;
using System.Text;
using Memento.Documents.Agenda;
using Memento.Documents.Render;
using Memento.Documents.Tests.Support;

namespace Memento.Documents.Tests.Fuzz;

/// <summary>
/// Generated worst cases for the text parsers and the viewer's HTML reader: inputs that make naive regexes,
/// string slicing and recursion quadratic, cubic or stack-deep. Each must finish in under <see cref="Limit"/> with a
/// result or an agenda error, never a crash.
/// </summary>
public sealed class PathologicalInputTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);

    public static TheoryData<string> TextCases => new(TextInputs.Keys);

    public static TheoryData<string> HtmlCases => new(HtmlInputs.Keys);

    private static Dictionary<string, Func<string>> TextInputs { get; } = new(StringComparer.Ordinal)
    {
        ["whitespace between words"] = () => "1. Welcome" + new string(' ', 200_000) + "everyone 10:00\n2. Budget" + new string('\t', 100_000) + "x\n3. Wrap-up",
        ["whitespace before a time"] = () => "Welcome" + new string(' ', 200_000) + "x\n" + "10" + new string(' ', 200_000) + "x\n1. Item",
        ["dot leaders"] = () => "1. Welcome " + new string('.', 200_000) + " x\n2. Close " + new string('…', 100_000) + "x",
        ["quote marks"] = () => new string('>', 100_000) + " 1. Item\n" + string.Concat(Enumerable.Repeat("> ", 50_000)) + "2. Next",
        ["emphasis markers"] = () => "# Agenda\n" + string.Concat(Enumerable.Repeat("*a ", 100_000)) + "\n- [ ] " + string.Concat(Enumerable.Repeat("_a ", 100_000)),
        ["bold and code markers"] = () => "# Agenda\n- " + string.Concat(Enumerable.Repeat("**a ", 60_000)) + "\n- " + string.Concat(Enumerable.Repeat("`a ", 60_000)) + "\n- " + string.Concat(Enumerable.Repeat("[a](", 60_000)),
        ["heading padded with spaces"] = () => "# a" + new string(' ', 200_000) + "b\n## " + new string('#', 100_000) + " x\n- [ ] item",
        ["indented continuation flood"] = () => "1. Start\n2. Next\n" + string.Concat(Enumerable.Repeat("  continued words\n", 500_000)),
        ["indentation staircase"] = () => string.Concat(Enumerable.Range(0, 20_000).Select(i => new string(' ', i % 2_000) + (i % 3 == 0 ? "a. " : i % 3 == 1 ? "i. " : "1. ") + "item\n")),
        ["one wide row then many rows"] = () => new string(',', 1_000_000) + "\n" + string.Concat(Enumerable.Repeat("x\n", 100_000)),
        ["one wide tab row then many rows"] = () => "Item\tTime\n" + new string('\t', 1_000_000) + "\n" + string.Concat(Enumerable.Repeat("x\t1\n", 100_000)),
        ["markdown table with many pipes"] = () => "a|b\n---|---\n" + new string('|', 1_000_000) + "\n" + string.Concat(Enumerable.Repeat("|x|y|\n", 100_000)),
        ["many numbered lines"] = () => string.Concat(Enumerable.Range(1, 100_000).Select(i => $"{i % 999}. Item\n")),
    };

    private static Dictionary<string, Func<string>> HtmlInputs { get; } = new(StringComparer.Ordinal)
    {
        ["nested div"] = () => string.Concat(Enumerable.Repeat("<div>", 200_000)) + "text",
        ["nested inline"] = () => string.Concat(Enumerable.Repeat("<b><i>", 100_000)) + "text",
        ["nested list"] = () => string.Concat(Enumerable.Repeat("<ul><li>", 100_000)) + "item",
        ["nested table"] = () => string.Concat(Enumerable.Repeat("<table><tr><td>", 60_000)) + "cell",
        ["nested article"] = () => "<article class=\"paper\">" + string.Concat(Enumerable.Repeat("<section class=\"paper-row\"><section class=\"paper-module\">", 50_000)) + "x",
        ["stray end tags"] = () => string.Concat(Enumerable.Repeat("<div>", 300)) + string.Concat(Enumerable.Repeat("</p>", 200_000)),
        ["many text nodes"] = () => "<p>" + string.Concat(Enumerable.Repeat("a<!---->", 125_000)) + "</p>",
        ["many attributes"] = () => "<p " + string.Concat(Enumerable.Range(0, 50_000).Select(i => $"a{i}=\"x\" ")) + ">x</p>",
    };

    [Theory]
    [MemberData(nameof(TextCases))]
    public async Task PastedTextFinishesQuickly(string name)
    {
        var text = TextInputs[name]();
        await Within(() => Agendas.Importer().ParseTextAsync(text, null, CancellationToken.None));
    }

    [Theory]
    [MemberData(nameof(TextCases))]
    public async Task TextFilesFinishQuickly(string name)
    {
        var bytes = Encoding.UTF8.GetBytes(TextInputs[name]());
        foreach (var file in new[] { "agenda.txt", "agenda.md", "agenda.csv", "agenda.tsv" })
        {
            await Within(() => Agendas.ImportAsync(bytes, file), file);
        }
    }

    [Theory]
    [MemberData(nameof(HtmlCases))]
    public async Task ViewerMarkupFinishesQuicklyWithoutACrash(string name)
    {
        var html = HtmlInputs[name]();
        await Within(() => Task.Run(() =>
        {
            HtmlToBlocks.ParseBlocks(html);
            HtmlToBlocks.ParsePaper(html);
        }));
    }

    private static async Task Within(Func<Task> parse, string what = "parsing")
    {
        var watch = Stopwatch.StartNew();
        try
        {
            await parse().WaitAsync(Limit * 6);
        }
        catch (AgendaImportException)
        {
            // Refusing the input is fine; crashing or hanging is not.
        }

        watch.Stop();
        Assert.True(watch.Elapsed < Limit, $"{what} took {watch.Elapsed.TotalSeconds:0.0} s");
    }
}
