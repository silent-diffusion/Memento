using System.Text;
using Memento.Documents.Styling;

namespace Memento.Documents.Render;

/// <summary>
/// The paper's stylesheet. The rules are fixed; a style only sets the custom properties and classes on the
/// <c>article.paper</c> element (<see cref="Variables"/>, <see cref="Classes"/>), so many papers can share one stylesheet.
/// Sizes derive from <c>--paper-base</c>, so a module's text size (<c>size-smaller</c>, <c>size-larger</c>) scales its text
/// but not the headings.
/// </summary>
public static class PaperCss
{
    /// <summary>The viewer shows the paper at the Document viewer's scale: Normal is 14 px there (DESIGN.md §12: title 28/800).</summary>
    public const double ViewerScale = 14.0 / 12.0;

    /// <summary>The Builder preview is about 400 px wide: Normal is 11 px there.</summary>
    public const double SkeletonScale = 11.0 / 12.0;

    public const string Stylesheet = """
.paper{background:#FFFFFF;color:#1D1C1A;box-sizing:border-box;display:flex;flex-direction:column;gap:var(--paper-gap);font-family:var(--paper-body-font);font-size:var(--paper-base);line-height:1.55;text-align:left;overflow-wrap:break-word;-webkit-print-color-adjust:exact;print-color-adjust:exact}
.paper *{box-sizing:border-box}
.paper p,.paper ul,.paper ol,.paper dl,.paper dd,.paper blockquote,.paper table,.paper figure{margin:0}
.paper-viewer{width:100%;max-width:820px;padding:56px 64px 72px;border-radius:12px}
.paper-skeleton{width:100%;padding:36px 36px 44px;border-radius:12px;min-height:560px}
.paper-sample{width:100%;margin:0 auto;border-radius:12px}
.paper-sample[data-paper="letter"]{max-width:640px;padding:52px 56px 40px}
.paper-sample[data-paper="a4"]{max-width:620px;padding:56px 52px 40px}
.paper-titleblock{display:flex;flex-direction:column;gap:calc(var(--paper-base) * 0.5)}
.paper-title{margin:0;font-family:var(--paper-head-font);font-size:calc(var(--paper-base) * 2);font-weight:800;letter-spacing:-0.02em;line-height:1.15;color:var(--paper-head)}
.paper.title-rule .paper-title{padding-bottom:calc(var(--paper-base) * 0.85);border-bottom:2px solid var(--paper-head)}
.paper-meta{font-size:calc(var(--paper-base) * 0.85);color:#6B6861}
.paper-row{display:grid;grid-template-columns:minmax(0,1fr);column-gap:var(--paper-col-gap);row-gap:var(--paper-gap);align-items:start}
.paper-row[data-cols="2"]{grid-template-columns:repeat(2,minmax(0,1fr))}
.paper-row[data-cols="3"]{grid-template-columns:repeat(3,minmax(0,1fr))}
.paper.lines .paper-row+.paper-row{border-top:1px solid #E2DFD8;padding-top:var(--paper-gap)}
.paper-module{min-width:0;display:flex;flex-direction:column;gap:calc(var(--paper-base) * 0.6);font-size:var(--paper-base)}
.paper-module.size-smaller{font-size:calc(var(--paper-base) * 0.875)}
.paper-module.size-larger{font-size:calc(var(--paper-base) * 1.15)}
.paper-h{margin:0;font-family:var(--paper-head-font);font-size:calc(var(--paper-base) * 1.05);font-weight:800;line-height:1.2;color:var(--paper-head)}
.paper.caps .paper-h{font-size:calc(var(--paper-base) * 0.8);text-transform:uppercase;letter-spacing:0.1em}
.paper.numbered{counter-reset:paper-sec}
.paper.numbered .paper-h::before{counter-increment:paper-sec;content:counter(paper-sec) ".\00A0\00A0"}
.paper-sub{margin:0;font-family:var(--paper-head-font);font-weight:700;line-height:1.25;color:var(--paper-head)}
h3.paper-sub{font-size:1.05em}
h4.paper-sub{font-size:1em}
h5.paper-sub{font-size:0.95em;font-weight:600}
.paper ul,.paper ol{padding-left:1.5em;display:flex;flex-direction:column;gap:0.35em}
.paper li>ul,.paper li>ol{margin-top:0.35em}
.paper-table{border-collapse:collapse;width:100%;font-size:0.92em}
.paper-table th{text-align:left;font-weight:700;padding:0.5em 0.75em;color:#1D1C1A;vertical-align:bottom}
.paper.th-fill .paper-table th{background:var(--paper-tint);color:var(--paper-head)}
.paper-table td{padding:0.5em 0.75em;border-bottom:1px solid #E2DFD8;vertical-align:top}
.paper .chips{display:flex;flex-wrap:wrap;gap:6px}
.paper .chip{display:inline-flex;align-items:center;min-height:1.85em;padding:0 0.75em;border-radius:999px;background:#EDEBE6;font-size:0.92em;font-weight:600;color:#1D1C1A;line-height:1.3}
.paper .kv{display:grid;grid-template-columns:auto minmax(0,1fr);gap:0.35em 1em}
.paper .kv dt{font-weight:700}
.paper-quote{border-left:2px solid #B9B5AC;padding-left:0.85em;display:flex;flex-direction:column;gap:0.35em}
.paper-quote footer{font-size:0.85em;color:#6B6861}
.paper-quote cite{font-style:normal}
.paper .timeline{list-style:none;padding-left:0;gap:0.45em}
.paper .timeline li{display:grid;grid-template-columns:4.5em minmax(0,1fr);gap:0.75em;align-items:baseline}
.paper .tl-t{font-family:'JetBrains Mono', Consolas, 'Cascadia Mono', monospace;font-size:0.85em;color:#5E5B55;text-decoration:none}
.paper .ts{display:inline-flex;align-items:center;height:18px;padding:0 6px;margin-left:6px;border-radius:999px;background:#EDEBE6;color:#5E5B55;font-family:'JetBrains Mono', Consolas, 'Cascadia Mono', monospace;font-size:10px;font-weight:500;line-height:1;text-decoration:none;vertical-align:middle;font-style:normal}
.paper a.ts:hover,.paper a.tl-t:hover{background:#FBE9E0;color:#9A3412}
.paper .note{color:#6B6861}
.paper-transcript{border-collapse:collapse;width:100%;font-size:0.92em}
.paper-transcript td{padding:0.3em 0.75em 0.3em 0;vertical-align:baseline;border-bottom:1px solid #E2DFD8}
.paper-transcript td.tr-x{padding-right:0}
.paper-transcript .tr-t{width:4.5em;font-family:'JetBrains Mono', Consolas, 'Cascadia Mono', monospace;font-size:0.85em;color:#5E5B55;white-space:nowrap}
.paper-transcript .tr-sp{width:9em;font-weight:700}
.paper-transcript .tr-ch th{text-align:left;font-family:var(--paper-head-font);font-weight:700;color:var(--paper-head);padding:0.9em 0 0.3em}
.paper-runhead{display:flex;justify-content:space-between;gap:16px;font-size:calc(var(--paper-base) * 0.78);color:#8A867E;padding-bottom:8px;border-bottom:1px solid #E2DFD8}
.paper-pagenum{margin-top:auto;padding-top:16px;text-align:center;font-size:calc(var(--paper-base) * 0.78);color:#8A867E}
.paper-empty{margin:0;font-size:12px;color:#8A867E}
.paper .sk{display:flex;flex-direction:column;gap:6px}
.paper .bar{height:6px;border-radius:3px;background:#DCD9D2}
.paper .bar.d{background:#B9B5AC}
.paper .cell{height:14px;border-radius:3px;background:#E8E6E0}
.paper .cell.h{background:#D2CFC7}
.paper.th-fill .cell.h{background:var(--paper-tint)}
.paper .sk-list{gap:7px}
.paper .sk-li,.paper .sk-tl,.paper .sk-tr{display:flex;gap:8px;align-items:center}
.paper .sk-fill{flex:1}
.paper .sk-dot{width:5px;height:5px}
.paper .sk-node{width:6px;height:6px;border-radius:50%}
.paper .sk-tl-t{width:24px;height:5px}
.paper .sk-table{display:grid;gap:4px}
.paper .sk-chips{flex-direction:row;flex-wrap:wrap;gap:5px}
.paper .sk-chips .cell{border-radius:999px}
.paper .sk-kv{display:grid;grid-template-columns:56px 1fr;gap:6px 10px}
.paper .sk-quote{flex-direction:row;gap:10px}
.paper .sk-quote-bar{width:2px;background:#B9B5AC;border-radius:1px}
.paper .sk-col{flex:1;display:flex;flex-direction:column;gap:6px}
.paper .sk-tl-list,.paper .sk-tr-list{gap:8px}

""";

    /// <summary>Extra rules for the print HTML (PDF).</summary>
    public const string PrintStylesheet = """
html,body{margin:0;padding:0;background:#FFFFFF}
.paper-print{width:100%;padding:0}
.paper-print .paper-h,.paper-print .paper-sub,.paper-print .paper-title{break-after:avoid;page-break-after:avoid}
.paper-print tr,.paper-print li,.paper-print .paper-quote{break-inside:avoid;page-break-inside:avoid}
.paper-print thead{display:table-header-group}
.paper-print .fn{font-size:0.7em;line-height:0;vertical-align:super;font-weight:600;color:#5E5B55}
.paper-print .fn a{color:inherit;text-decoration:none}
.paper-print .paper-footnotes{border-top:1px solid #E2DFD8;padding-top:calc(var(--paper-base) * 0.6);font-size:calc(var(--paper-base) * 0.8);color:#5E5B55;break-inside:auto}
.paper-print .paper-footnotes ol{gap:0.15em;padding-left:1.8em}

""";

    /// <summary>The classes on <c>article.paper</c> for a style in a mode.</summary>
    public static string Classes(DocumentStyle style, PaperMode mode)
    {
        ArgumentNullException.ThrowIfNull(style);
        var builder = new StringBuilder("paper paper-").Append(ModeName(mode));
        if (style.HeadingCase == HeadingCase.SmallCaps)
        {
            builder.Append(" caps");
        }

        if (style.NumberedHeadings)
        {
            builder.Append(" numbered");
        }

        if (style.RuleUnderTitle)
        {
            builder.Append(" title-rule");
        }

        if (style.LinesBetweenSections)
        {
            builder.Append(" lines");
        }

        if (style.TableHeaderFill)
        {
            builder.Append(" th-fill");
        }

        return builder.ToString();
    }

    /// <summary>The inline custom properties on <c>article.paper</c>: base size, spacing, colours and font stacks.</summary>
    public static string Variables(DocumentStyle style, PaperMode mode)
    {
        ArgumentNullException.ThrowIfNull(style);
        var (baseSize, gap, columnGap) = mode switch
        {
            PaperMode.Viewer => (Px(style.ScreenBasePx() * ViewerScale), Px(style.SpacingPx() * ViewerScale), "28px"),
            PaperMode.Skeleton => (Px(style.ScreenBasePx() * SkeletonScale), Px(style.SpacingPx()), "18px"),
            PaperMode.Print => (Pt(style.PrintBasePt()), Pt(style.SpacingPt()), "21pt"),
            _ => (Px(style.ScreenBasePx()), Px(style.SpacingPx()), "24px"),
        };

        return $"--paper-base:{baseSize};--paper-gap:{gap};--paper-col-gap:{columnGap};--paper-head:{style.HeadingHex()};--paper-tint:{style.TintHex()};"
            + $"--paper-head-font:{StyleMetrics.CssStack(style.HeadingTypeface)};--paper-body-font:{StyleMetrics.CssStack(style.BodyTypeface)}";
    }

    public static string ModeName(PaperMode mode) => mode switch
    {
        PaperMode.Skeleton => "skeleton",
        PaperMode.Sample => "sample",
        PaperMode.Print => "print",
        _ => "viewer",
    };

    private static string Px(double value) => HtmlText.Number(value) + "px";

    private static string Pt(double value) => HtmlText.Number(value) + "pt";
}
