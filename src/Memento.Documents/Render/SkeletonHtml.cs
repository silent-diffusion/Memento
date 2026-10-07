using System.Text;
using Memento.Documents.Model.Modules;

namespace Memento.Documents.Render;

/// <summary>The Builder preview's grey bars per content shape (DESIGN.md §10 and Builder.dc.html).</summary>
internal static class SkeletonHtml
{
    public static void Write(StringBuilder output, ModuleDefinition? definition)
    {
        switch (definition?.Shape ?? ContentShape.Paragraph)
        {
            case ContentShape.List:
                output.Append("<div class=\"sk sk-list\" aria-hidden=\"true\">")
                    .Append("<div class=\"sk-li\"><span class=\"bar d sk-dot\"></span><div class=\"bar sk-fill\"></div></div>")
                    .Append("<div class=\"sk-li\"><span class=\"bar d sk-dot\"></span><div class=\"bar sk-fill\" style=\"max-width:80%\"></div></div>")
                    .Append("<div class=\"sk-li\"><span class=\"bar d sk-dot\"></span><div class=\"bar sk-fill\" style=\"max-width:64%\"></div></div>")
                    .Append("</div>\n");
                break;
            case ContentShape.Table:
                var widths = definition!.ColumnWidths.Count == definition.Columns.Count && definition.Columns.Count > 0
                    ? definition.ColumnWidths
                    : [2, 1, 1];
                var columns = string.Join(' ', widths.Select(w => HtmlText.Number(w) + "fr"));
                output.Append("<div class=\"sk sk-table\" aria-hidden=\"true\" style=\"grid-template-columns:").Append(columns).Append("\">");
                for (var row = 0; row < 3; row++)
                {
                    foreach (var unused in widths)
                    {
                        output.Append(row == 0 ? "<div class=\"cell h\"></div>" : "<div class=\"cell\"></div>");
                    }
                }

                output.Append("</div>\n");
                break;
            case ContentShape.Chips:
                output.Append("<div class=\"sk sk-chips\" aria-hidden=\"true\">")
                    .Append("<span class=\"cell\" style=\"width:64px\"></span><span class=\"cell\" style=\"width:78px\"></span>")
                    .Append("<span class=\"cell\" style=\"width:52px\"></span><span class=\"cell\" style=\"width:40px\"></span>")
                    .Append("</div>\n");
                break;
            case ContentShape.LabelValue:
                output.Append("<div class=\"sk sk-kv\" aria-hidden=\"true\">")
                    .Append("<div class=\"bar d\" style=\"width:40px\"></div><div class=\"bar\"></div>")
                    .Append("<div class=\"bar d\" style=\"width:48px\"></div><div class=\"bar\" style=\"width:70%\"></div>")
                    .Append("</div>\n");
                break;
            case ContentShape.Quote:
                output.Append("<div class=\"sk sk-quote\" aria-hidden=\"true\"><span class=\"sk-quote-bar\"></span><div class=\"sk-col\">")
                    .Append("<div class=\"bar\"></div><div class=\"bar\" style=\"width:72%\"></div><div class=\"bar d\" style=\"width:28%;height:5px\"></div>")
                    .Append("</div></div>\n");
                break;
            case ContentShape.Timeline:
                output.Append("<div class=\"sk sk-tl-list\" aria-hidden=\"true\">")
                    .Append("<div class=\"sk-tl\"><span class=\"bar d sk-tl-t\"></span><span class=\"bar d sk-node\"></span><div class=\"bar sk-fill\"></div></div>")
                    .Append("<div class=\"sk-tl\"><span class=\"bar d sk-tl-t\"></span><span class=\"bar d sk-node\"></span><div class=\"bar sk-fill\" style=\"max-width:70%\"></div></div>")
                    .Append("</div>\n");
                break;
            case ContentShape.Transcript:
                output.Append("<div class=\"sk sk-tr-list\" aria-hidden=\"true\">")
                    .Append("<div class=\"sk-tr\"><span class=\"bar d sk-tl-t\"></span><span class=\"bar d\" style=\"width:44px\"></span><div class=\"bar sk-fill\"></div></div>")
                    .Append("<div class=\"sk-tr\"><span class=\"bar d sk-tl-t\"></span><span class=\"bar d\" style=\"width:36px\"></span><div class=\"bar sk-fill\" style=\"max-width:76%\"></div></div>")
                    .Append("<div class=\"sk-tr\"><span class=\"bar d sk-tl-t\"></span><span class=\"bar d\" style=\"width:44px\"></span><div class=\"bar sk-fill\" style=\"max-width:58%\"></div></div>")
                    .Append("</div>\n");
                break;
            case ContentShape.Text:
                output.Append("<div class=\"sk sk-text\" aria-hidden=\"true\"><div class=\"bar\" style=\"width:90%\"></div><div class=\"bar\" style=\"width:60%\"></div></div>\n");
                break;
            default:
                output.Append("<div class=\"sk sk-para\" aria-hidden=\"true\">")
                    .Append("<div class=\"bar\"></div><div class=\"bar\" style=\"width:94%\"></div><div class=\"bar\" style=\"width:88%\"></div><div class=\"bar\" style=\"width:56%\"></div>")
                    .Append("</div>\n");
                break;
        }
    }
}
