using Memento.Documents.Model;
using Memento.Documents.Model.Blocks;
using Memento.Documents.Render;
using Memento.Documents.Styling;
using Memento.Documents.Tests.DocumentModel.Support;

namespace Memento.Documents.Tests.DocumentModel;

public sealed class HtmlToBlocksTests
{
    private static readonly DocumentHtmlRenderer Renderer = new();

    public static TheoryData<string, string> FixturesAndStyles
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var fixture in new[] { "meeting-minutes", "all-shapes" })
            {
                foreach (var style in BuiltInStyles.Ids)
                {
                    data.Add(fixture, style);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(FixturesAndStyles))]
    public void ViewerMarkupRoundTripsToTheSameDocument(string fixture, string styleId)
    {
        var original = fixture == "meeting-minutes" ? SampleDocuments.MeetingMinutes() : SampleDocuments.AllShapes();
        var html = Renderer.RenderViewer(original, BuiltInStyles.Get(styleId)).Html;
        var applied = HtmlToBlocks.Apply(original, html);
        Assert.Equal(DocumentJson.Serialize(original), DocumentJson.Serialize(applied));
    }

    [Fact]
    public void ParsePaperReadsRowsModulesAndSettings()
    {
        var paper = HtmlToBlocks.ParsePaper(Renderer.RenderViewer(SampleDocuments.MeetingMinutes(), BuiltInStyles.Corporate).Html);
        Assert.Equal("Design review: library screen", paper.Title);
        Assert.Equal([1, 2, 1, 1, 2, 2, 1], paper.Rows.Select(r => r.Count));
        var summary = paper.Rows[0][0];
        Assert.Equal(("m01", "executiveSummary", "Executive summary", TextSize.Larger, true), (summary.Id, summary.Type, summary.Title, summary.TextSize, summary.LinkToTranscript));
        Assert.Equal(TextSize.Smaller, paper.Rows[^1][0].TextSize);
    }

    [Fact]
    public void ContentEditableMarkupBecomesBlocks()
    {
        const string html =
            "<h2>Notes&nbsp;from the call</h2>" +
            "<div>First line with <b>bold</b> and <i>italic</i> and <b><i>both</i></b>.</div>" +
            "<div>Second&nbsp;&nbsp;line<br>wrapped <span style=\"font-weight: 700\">heavy</span><a class=\"ts\" data-t=\"1122.5\" contenteditable=\"false\">18:42</a></div>" +
            "<div><br></div>" +
            "<ul><li>One<ul><li>One a</li></ul></li><li>Two <em>em</em></li></ul>" +
            "<ol><li>First<li>Second</ol>" +
            "<table class=\"paper-table\"><thead><tr><th>Action</th><th>Owner</th></tr></thead><tbody><tr><td>Send it<a class=\"ts\" data-t=\"60\">1:00</a></td><td>Kim</td></tr></tbody></table>" +
            "Loose text at the end";

        var blocks = HtmlToBlocks.ParseBlocks(html);

        Assert.Collection(
            blocks,
            b => Assert.Equal("Notes from the call", Assert.IsType<HeadingBlock>(b).Runs.Single().Text),
            b =>
            {
                var runs = Assert.IsType<ParagraphBlock>(b).Runs;
                Assert.Equal(["First line with ", "bold", " and ", "italic", " and ", "both", "."], runs.Select(r => r.Text));
                Assert.Equal(EmphasisStyle.Bold, runs[1].Style);
                Assert.Equal(EmphasisStyle.Italic, runs[3].Style);
                Assert.Equal(EmphasisStyle.BoldItalic, runs[5].Style);
            },
            b =>
            {
                var runs = Assert.IsType<ParagraphBlock>(b).Runs;
                Assert.Equal("Second line\nwrapped ", runs[0].Text);
                Assert.True(runs[1].IsBold);
                Assert.Equal((RunKind.Timestamp, 1122.5, "18:42"), (runs[2].Kind, runs[2].T!.Value, runs[2].Text));
            },
            b =>
            {
                var list = Assert.IsType<ListBlock>(b);
                Assert.Equal(ListStyle.Bulleted, list.Style);
                Assert.Equal("One a", list.Items[0].Items.Single().Runs.Single().Text);
                Assert.Equal(EmphasisStyle.Italic, list.Items[1].Runs[1].Style);
            },
            b => Assert.Equal(["First", "Second"], Assert.IsType<ListBlock>(b).Items.Select(i => i.Runs.Single().Text)),
            b =>
            {
                var table = Assert.IsType<TableBlock>(b);
                Assert.Equal(["Action", "Owner"], table.Columns);
                Assert.Equal(RunKind.Timestamp, table.Rows[0].Cells[0].Runs[1].Kind);
                Assert.Equal("Kim", table.Rows[0].Cells[1].Runs.Single().Text);
            },
            b => Assert.Equal("Loose text at the end", Assert.IsType<ParagraphBlock>(b).Runs.Single().Text));
    }

    [Fact]
    public void EditsInTheViewerUpdateTheDocumentAndMarkTheModuleEdited()
    {
        var original = SampleDocuments.MeetingMinutes();
        var html = Renderer.RenderViewer(original, BuiltInStyles.Corporate).Html
            .Replace("Design review: library screen</h1>", "Design review: library screen (final)</h1>", StringComparison.Ordinal)
            .Replace("Agree the library layout before build starts.", "Agree the <b>library layout</b> before build starts.", StringComparison.Ordinal)
            .Replace("<li>Dark theme scope</li>", "<li>Dark theme scope</li><li>Typography</li>", StringComparison.Ordinal);

        var applied = HtmlToBlocks.Apply(original, html);

        Assert.Equal("Design review: library screen (final)", applied.Title);
        var purpose = applied.Rows[1].Modules[0];
        Assert.True(purpose.Provenance.Edited);
        Assert.Equal(["c3"], purpose.Provenance.ClaimIds);
        Assert.Equal("library layout", ((ParagraphBlock)purpose.Blocks[0]).Runs[1].Text);
        var agenda = (ListBlock)applied.Rows[2].Modules[0].Blocks[0];
        Assert.Equal(5, agenda.Items.Count);
        Assert.False(applied.Rows[0].Modules[0].Provenance.Edited);
        Assert.Equal(original.Generation, applied.Generation);
        Assert.Equal(original.Meta, applied.Meta);
    }

    [Fact]
    public void ToolbarInsertsSurviveTheRoundTrip()
    {
        var original = SampleDocuments.AllShapes();
        var html = Renderer.RenderViewer(original, BuiltInStyles.Minimal).Html.Replace(
            "<p>After an hour",
            "<h2>Inserted heading</h2><table class=\"paper-table\"><thead><tr><th>A</th><th>B</th></tr></thead><tbody><tr><td>1</td><td>2</td></tr></tbody></table><p>After an hour",
            StringComparison.Ordinal);

        var module = HtmlToBlocks.Apply(original, html).Rows[0].Modules[0];
        var heading = Assert.IsType<HeadingBlock>(module.Blocks[3]);
        Assert.Equal(1, heading.Level);
        var table = Assert.IsType<TableBlock>(module.Blocks[4]);
        Assert.Equal(["A", "B"], table.Columns);
        Assert.Equal("2", table.Rows[0].Cells[1].Runs[0].Text);
        Assert.Equal(ProvenanceKind.User, module.Provenance.Kind);
        Assert.False(module.Provenance.Edited);
    }

    [Fact]
    public void UnknownBlocksAreKeptWhenTheViewerSavesEdits()
    {
        var doc = DocumentJson.Deserialize(DocumentJson.Serialize(SampleDocuments.AllShapes()).Replace(
            "\"blocks\": [",
            "\"blocks\": [ { \"type\": \"diagram\", \"nodes\": 3 },",
            StringComparison.Ordinal));
        var html = Renderer.RenderViewer(doc, BuiltInStyles.Corporate).Html;
        Assert.DoesNotContain("diagram", html, StringComparison.Ordinal);
        var applied = HtmlToBlocks.Apply(doc, html);
        Assert.All(applied.Modules(), m => Assert.IsType<UnknownBlock>(m.Blocks[^1]));
    }

    [Fact]
    public void MalformedMarkupDoesNotThrow()
    {
        var blocks = HtmlToBlocks.ParseBlocks("<p>Open <b>bold <i>nested</p><li>stray<td>cell</table></div><!-- c --><script>alert(1)</script>tail &amp; &lt;end&gt;");
        Assert.NotEmpty(blocks);
        Assert.DoesNotContain(blocks.OfType<ParagraphBlock>().SelectMany(p => p.Runs), r => r.Text.Contains("alert", StringComparison.Ordinal));
        Assert.Contains(blocks.OfType<ParagraphBlock>().SelectMany(p => p.Runs), r => r.Text.Contains("tail & <end>", StringComparison.Ordinal));
    }
}
