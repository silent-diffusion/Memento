using System.Text;
using Memento.Documents.Model;
using Memento.Documents.Model.Modules;
using Memento.Documents.Styling;
using Memento.Documents.Templates;

namespace Memento.Documents.Render;

/// <summary>
/// Renders documents to the paper markup of the Document viewer, the Builder preview (skeletons), the Style editor sample
/// and the print HTML that becomes the PDF. The same rows, columns, headings and per-module text sizes appear in every
/// mode; the output is deterministic (no clock, culture or random input).
/// </summary>
public sealed class DocumentHtmlRenderer
{
    private readonly ModuleCatalog _catalog;

    public DocumentHtmlRenderer(ModuleCatalog? catalog = null)
    {
        _catalog = catalog ?? ModuleCatalog.Default;
    }

    /// <summary>The Document viewer's paper. Text blocks are editable in place; data blocks and timestamp chips are marked <c>contenteditable="false"</c>.</summary>
    public RenderedPaper RenderViewer(Document document, DocumentStyle style)
    {
        ArgumentNullException.ThrowIfNull(document);
        var html = new StringBuilder();
        var writer = new BlockHtmlWriter(html, PaperMode.Viewer);
        Open(html, document, style, PaperMode.Viewer);
        TitleBlock(html, document.Title, MetaLine.Format(document.Meta), PaperMode.Viewer);
        Rows(html, document, writer);
        html.Append("</article>\n");
        return new RenderedPaper(PaperMode.Viewer, PaperCss.Stylesheet, html.ToString());
    }

    /// <summary>The Builder's live preview of a template: the real title and meta line, the headings in the style, a skeleton per module.</summary>
    public RenderedPaper RenderSkeleton(DocumentTemplate template, string title, DocumentMeta meta, DocumentStyle style)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(meta);
        var html = new StringBuilder();
        Open(html, null, style, PaperMode.Skeleton);
        TitleBlock(html, title, MetaLine.Format(meta), PaperMode.Skeleton);
        if (!template.Rows.Any(r => r.Modules.Count > 0))
        {
            html.Append("<p class=\"paper-empty\">Add modules to see the layout.</p>\n");
        }

        foreach (var row in template.Rows.Where(r => r.Modules.Count > 0))
        {
            html.Append("<div class=\"paper-row\" data-cols=\"").Append(row.Modules.Count).Append("\">\n");
            foreach (var module in row.Modules)
            {
                ModuleOpen(html, module.Id, module.Type, module.TextSize, module.LinkToTranscript, module.ResolveTitle(_catalog));
                SkeletonHtml.Write(html, _catalog.Find(module.Type));
                html.Append("</section>\n");
            }

            html.Append("</div>\n");
        }

        html.Append("</article>\n");
        return new RenderedPaper(PaperMode.Skeleton, PaperCss.Stylesheet, html.ToString());
    }

    /// <summary>The Style editor's sample page: fixed sample minutes with the running header and page number when the style shows them.</summary>
    public RenderedPaper RenderSample(DocumentStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        var document = SampleDocument.Minutes;
        var html = new StringBuilder();
        var writer = new BlockHtmlWriter(html, PaperMode.Sample);
        Open(html, document, style, PaperMode.Sample);
        if (style.RunningHeader)
        {
            var (left, right) = MetaLine.RunningHeader(document);
            html.Append("<div class=\"paper-runhead\"><span>").Append(HtmlText.Escape(left)).Append("</span><span>")
                .Append(HtmlText.Escape(right)).Append("</span></div>\n");
        }

        TitleBlock(html, document.Title, MetaLine.Format(document.Meta), PaperMode.Sample);
        Rows(html, document, writer);
        if (style.PageNumbers)
        {
            html.Append("<div class=\"paper-pagenum\">1 of 2</div>\n");
        }

        html.Append("</article>\n");
        return new RenderedPaper(PaperMode.Sample, PaperCss.Stylesheet, html.ToString());
    }

    /// <summary>
    /// A complete HTML page for PDF printing: <c>@page</c> size and margins from the style, the running header and
    /// "n of N" page numbers in the page margins, timestamps as numbered footnote references listed at the end.
    /// </summary>
    public string RenderPrintHtml(Document document, DocumentStyle style)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(style);
        var body = new StringBuilder();
        var writer = new BlockHtmlWriter(body, PaperMode.Print);
        Open(body, document, style, PaperMode.Print);
        TitleBlock(body, document.Title, MetaLine.Format(document.Meta), PaperMode.Print);
        Rows(body, document, writer);
        writer.FootnoteSection();
        body.Append("</article>\n");

        var page = new StringBuilder();
        page.Append("<!doctype html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n<title>").Append(HtmlText.Escape(document.Title))
            .Append("</title>\n<style>\n").Append(PageRules(document, style)).Append(PaperCss.Stylesheet).Append(PaperCss.PrintStylesheet)
            .Append("</style>\n</head>\n<body>\n").Append(body).Append("</body>\n</html>\n");
        return page.ToString();
    }

    /// <summary>The <c>@page</c> rules: paper size, margins, running header and page numbers.</summary>
    public static string PageRules(Document document, DocumentStyle style)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(style);
        var css = new StringBuilder();
        css.Append("@page{size:").Append(StyleMetrics.CssPageSize(style.Paper)).Append(";margin:")
            .Append(HtmlText.Number(StyleMetrics.MarginTopInches)).Append("in ").Append(HtmlText.Number(StyleMetrics.MarginSideInches)).Append("in ")
            .Append(HtmlText.Number(StyleMetrics.MarginBottomInches)).Append("in ").Append(HtmlText.Number(StyleMetrics.MarginSideInches)).Append("in");
        var marginFont = $"font-family:{StyleMetrics.CssStack(style.BodyTypeface)};font-size:{HtmlText.Number(style.PrintBasePt() * 0.78)}pt;color:{StyleMetrics.FaintInk}";
        if (style.RunningHeader)
        {
            var (left, right) = MetaLine.RunningHeader(document);
            css.Append(";@top-left{content:").Append(HtmlText.CssString(left)).Append(';').Append(marginFont)
                .Append(";width:60%;vertical-align:bottom;text-align:left;padding-bottom:6pt;margin-bottom:12pt;border-bottom:0.75pt solid ").Append(StyleMetrics.Hairline).Append('}');
            css.Append("@top-right{content:").Append(HtmlText.CssString(right)).Append(';').Append(marginFont)
                .Append(";width:40%;vertical-align:bottom;text-align:right;padding-bottom:6pt;margin-bottom:12pt;border-bottom:0.75pt solid ").Append(StyleMetrics.Hairline).Append('}');
        }

        if (style.PageNumbers)
        {
            css.Append("@bottom-center{content:counter(page) \" of \" counter(pages);").Append(marginFont).Append(";vertical-align:top;padding-top:6pt}");
        }

        css.Append("}\n");
        return css.ToString();
    }

    private static void Open(StringBuilder html, Document? document, DocumentStyle style, PaperMode mode)
    {
        ArgumentNullException.ThrowIfNull(style);
        html.Append("<article class=\"").Append(PaperCss.Classes(style, mode)).Append('"');
        if (document is not null && !string.IsNullOrEmpty(document.Id))
        {
            html.Append(" data-doc=\"").Append(HtmlText.Escape(document.Id)).Append('"');
        }

        html.Append(" data-style=\"").Append(HtmlText.Escape(style.Id)).Append("\" data-paper=\"").Append(style.Paper == PaperSize.A4 ? "a4" : "letter")
            .Append("\" style=\"").Append(HtmlText.Escape(PaperCss.Variables(style, mode))).Append("\">\n");
    }

    private static void TitleBlock(StringBuilder html, string title, string meta, PaperMode mode)
    {
        html.Append("<header class=\"paper-titleblock\"><h1 class=\"paper-title\">").Append(HtmlText.Escape(title)).Append("</h1>");
        if (meta.Length > 0)
        {
            html.Append("<p class=\"paper-meta\"").Append(mode == PaperMode.Viewer ? " contenteditable=\"false\"" : string.Empty).Append('>')
                .Append(HtmlText.Escape(meta)).Append("</p>");
        }

        html.Append("</header>\n");
    }

    private void Rows(StringBuilder html, Document document, BlockHtmlWriter writer)
    {
        foreach (var row in document.Rows.Where(r => r.Modules.Count > 0))
        {
            html.Append("<div class=\"paper-row\" data-cols=\"").Append(row.Modules.Count).Append("\">\n");
            foreach (var module in row.Modules)
            {
                var title = string.IsNullOrWhiteSpace(module.Title) ? _catalog.Find(module.Type)?.DisplayName ?? string.Empty : module.Title;
                ModuleOpen(html, module.Id, module.Type, module.TextSize, module.LinkToTranscript, title);
                foreach (var block in module.Blocks)
                {
                    writer.Write(block);
                }

                html.Append("</section>\n");
            }

            html.Append("</div>\n");
        }
    }

    private static void ModuleOpen(StringBuilder html, string id, string type, TextSize size, bool link, string title)
    {
        html.Append("<section class=\"paper-module size-").Append(size.Name()).Append("\" data-id=\"").Append(HtmlText.Escape(id))
            .Append("\" data-module=\"").Append(HtmlText.Escape(type)).Append("\" data-size=\"").Append(size.Name())
            .Append("\" data-link=\"").Append(link ? "true" : "false").Append("\">\n<h2 class=\"paper-h\">").Append(HtmlText.Escape(title)).Append("</h2>\n");
    }
}
