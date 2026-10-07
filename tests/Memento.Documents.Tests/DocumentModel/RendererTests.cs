using System.Text.RegularExpressions;
using Memento.Documents.Model;
using Memento.Documents.Model.Modules;
using Memento.Documents.Render;
using Memento.Documents.Styling;
using Memento.Documents.Templates;
using Memento.Documents.Tests.DocumentModel.Support;

namespace Memento.Documents.Tests.DocumentModel;

public sealed partial class RendererTests
{
    private static readonly DocumentHtmlRenderer Renderer = new();

    public static TheoryData<string> Presets => new(BuiltInStyles.Ids);

    [Theory]
    [MemberData(nameof(Presets))]
    public void ViewerSnapshotPerPreset(string styleId) =>
        Snapshot.Match($"meeting-minutes.{styleId}.viewer.html", Renderer.RenderViewer(SampleDocuments.MeetingMinutes(), BuiltInStyles.Get(styleId)).ToHtmlPage("Meeting minutes"));

    [Theory]
    [MemberData(nameof(Presets))]
    public void SkeletonSnapshotPerPreset(string styleId) =>
        Snapshot.Match(
            $"meeting-minutes.{styleId}.skeleton.html",
            Renderer.RenderSkeleton(BuiltInTemplates.MeetingMinutes, "Design review: library screen", SampleDocuments.MeetingMinutes().Meta, BuiltInStyles.Get(styleId)).ToHtmlPage("Builder preview"));

    [Theory]
    [MemberData(nameof(Presets))]
    public void PrintSnapshotPerPreset(string styleId) =>
        Snapshot.Match($"meeting-minutes.{styleId}.print.html", Renderer.RenderPrintHtml(SampleDocuments.MeetingMinutes(), BuiltInStyles.Get(styleId)));

    [Theory]
    [MemberData(nameof(Presets))]
    public void StyleEditorSampleSnapshotPerPreset(string styleId) =>
        Snapshot.Match($"style-sample.{styleId}.html", Renderer.RenderSample(BuiltInStyles.Get(styleId)).ToHtmlPage("Style sample"));

    [Fact]
    public void EveryShapeSnapshotInEveryMode()
    {
        var style = BuiltInStyles.Corporate;
        Snapshot.Match("all-shapes.viewer.html", Renderer.RenderViewer(SampleDocuments.AllShapes(), style).ToHtmlPage("All shapes"));
        Snapshot.Match("all-shapes.print.html", Renderer.RenderPrintHtml(SampleDocuments.AllShapes(), BuiltInStyles.Academic with { Paper = PaperSize.A4 }));
        Snapshot.Match("all-modules.skeleton.html", Renderer.RenderSkeleton(EveryModuleTemplate(), "Every module", new DocumentMeta { Kind = "Fixture" }, style).ToHtmlPage("All modules"));
    }

    [Fact]
    public void RenderingIsDeterministic()
    {
        var a = Renderer.RenderPrintHtml(SampleDocuments.MeetingMinutes(), BuiltInStyles.Corporate);
        var b = new DocumentHtmlRenderer().RenderPrintHtml(SampleDocuments.MeetingMinutes(), BuiltInStyles.Corporate);
        Assert.Equal(a, b);
    }

    [Fact]
    public void ViewerShowsTheDesignStructure()
    {
        var html = Renderer.RenderViewer(SampleDocuments.MeetingMinutes(), BuiltInStyles.Corporate).Html;
        Assert.Contains("<h1 class=\"paper-title\">Design review: library screen</h1>", html, StringComparison.Ordinal);
        Assert.Contains("Meeting minutes · Monday 5 October 2026, 4:00 PM · 1 h 10 min · Zoom", html, StringComparison.Ordinal);
        Assert.Equal(3, Count(html, "<div class=\"paper-row\" data-cols=\"2\">"));
        Assert.Contains("<a class=\"ts\" href=\"#t=1122\" data-t=\"1122\" contenteditable=\"false\">18:42</a>", html, StringComparison.Ordinal);
        Assert.Contains("<span class=\"chip\">Priya Natarajan</span>", html, StringComparison.Ordinal);
        Assert.Contains("class=\"paper paper-viewer caps title-rule th-fill\"", html, StringComparison.Ordinal);
        Assert.Contains("--paper-base:14px", html, StringComparison.Ordinal);
        Assert.Contains("--paper-head:#1F3A5F", html, StringComparison.Ordinal);
    }

    [Fact]
    public void StylesSwitchClassesAndVariablesOnly()
    {
        var minimal = Renderer.RenderViewer(SampleDocuments.MeetingMinutes(), BuiltInStyles.Minimal).Html;
        var academic = Renderer.RenderViewer(SampleDocuments.MeetingMinutes(), BuiltInStyles.Academic).Html;
        Assert.Contains("class=\"paper paper-viewer lines\"", minimal, StringComparison.Ordinal);
        Assert.Contains("--paper-gap:32.667px", minimal, StringComparison.Ordinal);
        Assert.Contains("class=\"paper paper-viewer numbered\"", academic, StringComparison.Ordinal);
        Assert.Contains("Georgia", academic, StringComparison.Ordinal);
        Assert.Equal(
            Strip(minimal),
            Strip(academic));
    }

    [Fact]
    public void SkeletonDrawsEachShapeAsDescribed()
    {
        var html = Renderer.RenderSkeleton(BuiltInTemplates.MeetingMinutes, "Design review: library screen", SampleDocuments.MeetingMinutes().Meta, BuiltInStyles.Corporate).Html;
        Assert.Contains("<h1 class=\"paper-title\">Design review: library screen</h1>", html, StringComparison.Ordinal);
        Assert.Equal(6, Count(html, "class=\"paper-row\""));
        Assert.Contains("sk sk-table\" aria-hidden=\"true\" style=\"grid-template-columns:2fr 1fr 1fr\"", html, StringComparison.Ordinal);
        Assert.Equal(3, Count(html, "class=\"cell h\""));
        Assert.Contains("sk sk-chips", html, StringComparison.Ordinal);
        Assert.Equal(2, Count(html, "sk sk-kv"));
        Assert.Equal(2, Count(html, "sk sk-para"));
        Assert.Equal(3, Count(html, "sk sk-list"));
        Assert.Contains("<h2 class=\"paper-h\">Action items</h2>", html, StringComparison.Ordinal);

        var empty = Renderer.RenderSkeleton(BuiltInTemplates.MeetingMinutes with { Rows = [] }, "Untitled", new DocumentMeta(), BuiltInStyles.Minimal).Html;
        Assert.Contains("Add modules to see the layout.", empty, StringComparison.Ordinal);
    }

    [Fact]
    public void PrintHtmlCarriesPageRulesFootnotesAndColumns()
    {
        var doc = SampleDocuments.MeetingMinutes();
        var html = Renderer.RenderPrintHtml(doc, BuiltInStyles.Corporate);
        Assert.Contains("@page{size:letter;margin:0.9in 1in 0.9in 1in;@top-left{content:\"Design review: library screen\"", html, StringComparison.Ordinal);
        Assert.Contains("@top-right{content:\"5 October 2026\"", html, StringComparison.Ordinal);
        Assert.Contains("@bottom-center{content:counter(page) \" of \" counter(pages)", html, StringComparison.Ordinal);
        Assert.Contains("--paper-base:11pt", html, StringComparison.Ordinal);
        Assert.Equal(3, Count(html, "<div class=\"paper-row\" data-cols=\"2\">"));
        Assert.DoesNotContain("class=\"ts\"", html, StringComparison.Ordinal);

        var timestamps = doc.Modules().SelectMany(Runs).Count(r => r.Kind == RunKind.Timestamp);
        Assert.Equal(timestamps, Count(html, "<sup class=\"fn\""));
        Assert.Contains("<li id=\"fn-1\" data-t=\"1122\">18:42 — see the recording at 0:18:42</li>", html, StringComparison.Ordinal);
        Assert.Contains($"<li id=\"fn-{timestamps}\"", html, StringComparison.Ordinal);

        var plain = Renderer.RenderPrintHtml(doc, BuiltInStyles.Minimal);
        Assert.DoesNotContain("@top-left", plain, StringComparison.Ordinal);
        var a4 = Renderer.RenderPrintHtml(doc, BuiltInStyles.Minimal with { Paper = PaperSize.A4, PageNumbers = false });
        Assert.Contains("@page{size:A4;", a4, StringComparison.Ordinal);
        Assert.DoesNotContain("counter(page)", a4, StringComparison.Ordinal);
    }

    [Fact]
    public void SampleShowsRunningHeaderAndPageNumberWhenTheStyleDoes()
    {
        var corporate = Renderer.RenderSample(BuiltInStyles.Corporate).Html;
        Assert.Contains("<div class=\"paper-runhead\"><span>Design review: library screen</span><span>5 October 2026</span></div>", corporate, StringComparison.Ordinal);
        Assert.Contains("<div class=\"paper-pagenum\">1 of 2</div>", corporate, StringComparison.Ordinal);
        Assert.Contains("data-paper=\"letter\"", corporate, StringComparison.Ordinal);
        var minimal = Renderer.RenderSample(BuiltInStyles.Minimal with { Paper = PaperSize.A4, PageNumbers = false }).Html;
        Assert.DoesNotContain("paper-runhead", minimal, StringComparison.Ordinal);
        Assert.DoesNotContain("paper-pagenum", minimal, StringComparison.Ordinal);
        Assert.Contains("data-paper=\"a4\"", minimal, StringComparison.Ordinal);
    }

    [Fact]
    public void TextIsEscaped()
    {
        var html = Renderer.RenderViewer(SampleDocuments.AllShapes(), BuiltInStyles.Corporate).Html;
        Assert.Contains("&lt;tag&gt; &amp; &quot;quotes&quot;", html, StringComparison.Ordinal);
        Assert.Contains("pipe.<br>A second line.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<tag>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void OnPaperAFootnoteMarkerSitsOnTheWordBeforeIt()
    {
        var doc = SampleDocuments.AllShapes();
        var paragraph = new Model.Blocks.ParagraphBlock { Runs = [Run.Plain("Despite the absence of such gatherings. "), Run.Timestamp(105), Run.Plain(" They met.")] };
        var single = doc with { Rows = [DocumentRow.Of(doc.Rows[0].Modules[0] with { Blocks = [paragraph] })] };

        var print = Renderer.RenderPrintHtml(single, BuiltInStyles.Corporate);
        var viewer = Renderer.RenderViewer(single, BuiltInStyles.Corporate).Html;

        Assert.Contains("gatherings.<sup class=\"fn\"", print, StringComparison.Ordinal);
        Assert.Contains("gatherings. <a class=\"ts\"", viewer, StringComparison.Ordinal);
    }

    [Fact]
    public void ModuleWithoutATitleUsesTheCatalogName()
    {
        var doc = SampleDocuments.AllShapes();
        var untitled = doc with { Rows = [DocumentRow.Of(doc.Rows[0].Modules[0] with { Title = string.Empty, Type = ModuleIds.Notes })] };
        Assert.Contains("<h2 class=\"paper-h\">Notes</h2>", Renderer.RenderViewer(untitled, BuiltInStyles.Corporate).Html, StringComparison.Ordinal);
    }

    internal static IEnumerable<Run> Runs(ModuleBlock module) => module.Blocks.SelectMany(BlockRuns);

    internal static IEnumerable<Run> BlockRuns(Model.Blocks.Block block) => block switch
    {
        Model.Blocks.ParagraphBlock p => p.Runs,
        Model.Blocks.HeadingBlock h => h.Runs,
        Model.Blocks.ListBlock l => l.Items.SelectMany(ItemRuns),
        Model.Blocks.TableBlock t => t.Rows.SelectMany(r => r.Cells).SelectMany(c => c.Runs),
        Model.Blocks.LabelValueBlock kv => kv.Pairs.SelectMany(p => p.Runs),
        Model.Blocks.QuoteBlock q => q.Runs,
        Model.Blocks.TimelineBlock tl => tl.Entries.SelectMany(e => e.Runs),
        _ => [],
    };

    internal static DocumentTemplate EveryModuleTemplate()
    {
        var modules = ModuleCatalog.Default.All.Select((m, i) => TemplateModule.FromCatalog(m, $"t{i + 1:00}")).ToList();
        var rows = modules.Chunk(3).Select(chunk => new TemplateRow { Modules = chunk }).ToList();
        return BuiltInTemplates.MeetingMinutes with { Id = "every-module", Name = "Every module", Rows = rows };
    }

    private static IEnumerable<Run> ItemRuns(Model.Blocks.ListItem item) => item.Runs.Concat(item.Items.SelectMany(ItemRuns));

    private static int Count(string text, string value) => Regex.Matches(text, Regex.Escape(value)).Count;

    private static string Strip(string html) => StyleAttributes().Replace(html, string.Empty);

    [GeneratedRegex(" (class|style|data-style)=\"[^\"]*\"")]
    private static partial Regex StyleAttributes();
}
