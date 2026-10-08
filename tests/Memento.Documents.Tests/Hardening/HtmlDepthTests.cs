using System.Diagnostics;
using Memento.Documents.Render;
using Memento.Documents.Render.Html;
using Memento.Documents.Tests.Support;

namespace Memento.Documents.Tests.Hardening;

/// <summary>The viewer's edited markup (documents.saveEdit) is read without recursion deep enough to overflow the host's stack.</summary>
public sealed class HtmlDepthTests
{
    [Theory]
    [InlineData("<div>")]
    [InlineData("<b><i>")]
    [InlineData("<ul><li>")]
    [InlineData("<blockquote>")]
    public void AMegabyteOfNestingIsReadWithoutACrash(string open)
    {
        var html = string.Concat(Enumerable.Repeat(open, 1_000_000 / open.Length)) + "deep text";
        var watch = Stopwatch.StartNew();

        var blocks = HtmlToBlocks.ParseBlocks(html);
        var paper = HtmlToBlocks.ParsePaper(html);

        Assert.True(watch.Elapsed < WallClock.Limit(5), $"took {watch.Elapsed.TotalSeconds:0.0} s");
        Assert.NotNull(blocks);
        Assert.NotNull(paper);
    }

    [Fact]
    public void NestingStopsAtTheDepthLimit()
    {
        var root = HtmlParser.Parse(string.Concat(Enumerable.Repeat("<div>", 1_000)) + "x");

        var depth = 0;
        for (var node = root; node.Elements().Any(); node = node.Elements().Last())
        {
            depth++;
        }

        Assert.InRange(depth, HtmlParser.MaxDepth - 1, HtmlParser.MaxDepth + 1);
    }

    [Fact]
    public void FindSearchesDepthFirstInDocumentOrder()
    {
        var root = HtmlParser.Parse("<div><p class=\"a\">one</p></div><p class=\"a\">two</p><section><p class=\"a\">three</p></section>");

        Assert.Equal("one", root.Find(n => n.HasClass("a"))!.InnerText());
        Assert.Equal(["one", "two", "three"], root.FindAll(n => n.HasClass("a")).Select(n => n.InnerText()));
    }

    [Fact]
    public void ManyTextNodesMergeInLinearTime()
    {
        var html = "<p>" + string.Concat(Enumerable.Repeat("a<!---->", 125_000)) + "</p>";
        var watch = Stopwatch.StartNew();

        var blocks = HtmlToBlocks.ParseBlocks(html);

        Assert.True(watch.Elapsed < WallClock.Limit(5), $"took {watch.Elapsed.TotalSeconds:0.0} s");
        Assert.Single(blocks);
    }
}
