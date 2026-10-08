using Memento.Documents.Export;
using Memento.Documents.Model;
using Memento.Documents.Model.Blocks;
using Memento.Documents.Tests.DocumentModel.Support;

namespace Memento.Documents.Tests.Hardening;

/// <summary>A timestamp's label comes from edited viewer markup; in Markdown it must stay a label, never a link.</summary>
public sealed class MarkdownTimestampTests
{
    [Fact]
    public void ATimestampLabelCannotCloseTheBracketAndOpenALink()
    {
        var md = Export(Run.Plain("See "), Run.Timestamp(5, "0:05](javascript:alert(1)) [x"));

        Assert.Contains("See [0:05\\](javascript:alert(1)) \\[x]", md, StringComparison.Ordinal);
        Assert.DoesNotContain("05](", md, StringComparison.Ordinal);
    }

    [Fact]
    public void ATimestampLabelStaysOnOneLine()
    {
        var md = Export(Run.Timestamp(5, "0:05\n\n# Injected heading\n[a]: https://example.invalid"));

        Assert.DoesNotContain("\n# Injected", md, StringComparison.Ordinal);
        Assert.Contains("[0:05 \\# Injected heading \\[a\\]: https://example.invalid]", md, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("(https://example.invalid)", "[0:05]\\(https://example.invalid)")]
    [InlineData(": https://example.invalid", "[0:05]\\: https://example.invalid")]
    [InlineData(" and more", "[0:05] and more")]
    public void TextRightAfterATimestampDoesNotTurnItIntoALink(string after, string expected)
    {
        var md = Export(Run.Timestamp(5), Run.Plain(after));

        Assert.Contains(expected, md, StringComparison.Ordinal);
    }

    private static string Export(params Run[] runs)
    {
        var original = SampleDocuments.MeetingMinutes();
        var module = original.Rows[0].Modules[0] with { Blocks = [new ParagraphBlock { Runs = runs }] };
        return new MarkdownExporter().Export(original with { Rows = [DocumentRow.Of(module)] });
    }
}
