using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Memento.Documents.Model;
using Memento.Documents.Model.Blocks;
using Memento.Documents.Render.Html;

namespace Memento.Documents.Render;

/// <summary>
/// The viewer's light-editing contract: reads the paper markup back into blocks after in-place edits. It understands what
/// the renderer writes and what a WebView2 <c>contenteditable</c> adds (<c>&lt;b&gt;</c>, <c>&lt;i&gt;</c>, <c>&lt;div&gt;</c>
/// lines, <c>&amp;nbsp;</c>, a new heading, list or table, an inserted timestamp chip <c>&lt;a class="ts" data-t="…"&gt;</c>).
/// Whitespace is collapsed the way the browser shows it.
/// </summary>
public static partial class HtmlToBlocks
{
    private const char LineBreak = (char)0x2028;

    private static readonly HashSet<string> BlockTags = new(StringComparer.Ordinal)
    {
        "p", "div", "section", "article", "header", "footer", "main", "aside", "nav", "h1", "h2", "h3", "h4", "h5", "h6",
        "ul", "ol", "li", "table", "thead", "tbody", "tfoot", "tr", "td", "th", "dl", "dt", "dd", "blockquote", "figure", "hr", "pre",
    };

    /// <summary>The blocks of a fragment (one module's content, or anything the toolbar produced).</summary>
    public static IReadOnlyList<Block> ParseBlocks(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        return BlocksOf(HtmlParser.Parse(html).Children);
    }

    /// <summary>The title and the rows of modules of a rendered (and possibly edited) paper.</summary>
    public static ParsedPaper ParsePaper(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        var root = HtmlParser.Parse(html);
        var paper = root.Find(n => n.Is("article") && n.HasClass("paper")) ?? root;
        var titleNode = paper.Find(n => n.HasClass("paper-title")) ?? paper.Find(n => n.Is("h1"));
        var title = titleNode is null ? null : PlainText(titleNode);

        var rows = new List<IReadOnlyList<ParsedModule>>();
        var rowNodes = paper.FindAll(n => n.HasClass("paper-row")).ToList();
        if (rowNodes.Count == 0)
        {
            rows.AddRange(paper.FindAll(IsModule).Select(m => (IReadOnlyList<ParsedModule>)[Module(m)]));
        }
        else
        {
            foreach (var row in rowNodes)
            {
                var modules = row.FindAll(IsModule).Select(Module).ToList();
                if (modules.Count > 0)
                {
                    rows.Add(modules);
                }
            }
        }

        return new ParsedPaper(title, rows);
    }

    /// <summary>
    /// Applies the viewer's edited markup to <paramref name="original"/>: title, module headings and blocks come from the
    /// markup; ids, types, text sizes, provenance and anything the markup does not carry come from the original. A module
    /// whose content changed is marked <see cref="Provenance.Edited"/>. Version and modification time are the caller's.
    /// </summary>
    public static Document Apply(Document original, string editedHtml)
    {
        ArgumentNullException.ThrowIfNull(original);
        var parsed = ParsePaper(editedHtml);
        var originals = original.Modules().GroupBy(m => m.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var rows = new List<DocumentRow>(parsed.Rows.Count);
        foreach (var parsedRow in parsed.Rows)
        {
            var modules = new List<ModuleBlock>(parsedRow.Count);
            foreach (var p in parsedRow)
            {
                if (!originals.TryGetValue(p.Id, out var before))
                {
                    modules.Add(new ModuleBlock
                    {
                        Id = p.Id,
                        Type = p.Type,
                        Title = p.Title,
                        TextSize = p.TextSize,
                        LinkToTranscript = p.LinkToTranscript,
                        Provenance = Provenance.FromUser(),
                        Blocks = p.Blocks,
                    });
                    continue;
                }

                var blocks = p.Blocks.Concat(before.Blocks.OfType<UnknownBlock>()).ToList();
                var changed = !string.Equals(p.Title, before.Title, StringComparison.Ordinal) || !SameBlocks(blocks, before.Blocks);
                modules.Add(before with
                {
                    Title = p.Title,
                    Blocks = blocks,
                    Provenance = changed && before.Provenance.Kind != ProvenanceKind.User ? before.Provenance with { Edited = true } : before.Provenance,
                });
            }

            if (modules.Count > 0)
            {
                rows.Add(new DocumentRow { Modules = modules });
            }
        }

        return original with { Title = string.IsNullOrWhiteSpace(parsed.Title) ? original.Title : parsed.Title!, Rows = rows };
    }

    private static bool SameBlocks(IReadOnlyList<Block> a, IReadOnlyList<Block> b) =>
        string.Equals(
            JsonSerializer.Serialize(a, DocumentJsonContext.Default.IReadOnlyListBlock),
            JsonSerializer.Serialize(b, DocumentJsonContext.Default.IReadOnlyListBlock),
            StringComparison.Ordinal);

    private static bool IsModule(HtmlNode node) => node.HasClass("paper-module");

    private static ParsedModule Module(HtmlNode section)
    {
        var heading = section.Elements().FirstOrDefault(e => e.HasClass("paper-h"));
        var title = heading is null ? string.Empty : PlainText(heading);
        var content = section.Children.Where(c => !ReferenceEquals(c, heading));
        return new ParsedModule(
            section.Attr("data-id") ?? string.Empty,
            section.Attr("data-module") ?? string.Empty,
            title,
            TextSizeExtensions.ParseName(section.Attr("data-size")),
            string.Equals(section.Attr("data-link"), "true", StringComparison.Ordinal),
            BlocksOf(content));
    }

    private static List<Block> BlocksOf(IEnumerable<HtmlNode> nodes)
    {
        var blocks = new List<Block>();
        var pending = new List<HtmlNode>();
        foreach (var node in nodes)
        {
            if (node.IsText || !BlockTags.Contains(node.Name!))
            {
                pending.Add(node);
                continue;
            }

            FlushParagraph(pending, blocks);
            BlockOf(node, blocks);
        }

        FlushParagraph(pending, blocks);
        return blocks;
    }

    private static void FlushParagraph(List<HtmlNode> pending, List<Block> blocks)
    {
        if (pending.Count == 0)
        {
            return;
        }

        var runs = Runs(pending);
        pending.Clear();
        if (runs.Count > 0)
        {
            blocks.Add(new ParagraphBlock { Runs = runs });
        }
    }

    private static void BlockOf(HtmlNode node, List<Block> blocks)
    {
        switch (node.Name)
        {
            case "p":
                AddParagraph(node, blocks);
                break;
            case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                var runs = Runs(node.Children);
                if (runs.Count > 0)
                {
                    var level = node.Name switch { "h4" => 2, "h5" or "h6" => 3, _ => 1 };
                    blocks.Add(new HeadingBlock { Level = level, Runs = runs });
                }

                break;
            case "ul" or "ol":
                if (node.HasClass("timeline"))
                {
                    blocks.Add(Timeline(node));
                }
                else
                {
                    var list = List(node);
                    if (list.Items.Count > 0)
                    {
                        blocks.Add(list);
                    }
                }

                break;
            case "table":
                if (node.HasClass("paper-transcript"))
                {
                    blocks.Add(Transcript(node));
                }
                else
                {
                    blocks.Add(Table(node));
                }

                break;
            case "dl":
                blocks.Add(LabelValue(node));
                break;
            case "blockquote":
                blocks.Add(Quote(node));
                break;
            case "div" when node.HasClass("chips"):
                blocks.Add(new ChipsBlock { Items = node.FindAll(n => n.HasClass("chip")).Select(PlainText).Where(t => t.Length > 0).ToList() });
                break;
            case "div" when node.HasClass("sk") || node.HasClass("paper-runhead") || node.HasClass("paper-pagenum"):
                break;
            case "hr":
                break;
            case "section" when node.HasClass("paper-footnotes"):
                break;
            default:
                if (node.Children.Any(c => !c.IsText && BlockTags.Contains(c.Name!)))
                {
                    blocks.AddRange(BlocksOf(node.Children));
                }
                else
                {
                    AddParagraph(node, blocks);
                }

                break;
        }
    }

    private static void AddParagraph(HtmlNode node, List<Block> blocks)
    {
        var runs = Runs(node.Children);
        if (runs.Count > 0)
        {
            blocks.Add(new ParagraphBlock { Runs = runs });
        }
    }

    private static ListBlock List(HtmlNode list)
    {
        var items = new List<ListItem>();
        foreach (var child in list.Elements())
        {
            if (child.Is("li"))
            {
                items.Add(Item(child));
            }
            else if (child.Is("ul") || child.Is("ol"))
            {
                // A nested list typed directly inside the list belongs to the previous item.
                var nested = List(child).Items;
                if (items.Count > 0)
                {
                    items[^1] = items[^1] with { Items = items[^1].Items.Concat(nested).ToList() };
                }
                else
                {
                    items.AddRange(nested);
                }
            }
        }

        return new ListBlock { Style = list.Is("ol") ? ListStyle.Numbered : ListStyle.Bulleted, Items = items };
    }

    private static ListItem Item(HtmlNode li)
    {
        var inline = li.Children.Where(c => !(c.Is("ul") || c.Is("ol")));
        var nested = li.Elements().Where(c => c.Is("ul") || c.Is("ol")).SelectMany(c => List(c).Items).ToList();
        return new ListItem { Runs = Runs(inline), Items = nested };
    }

    private static TableBlock Table(HtmlNode table)
    {
        var rows = table.FindAll(n => n.Is("tr")).ToList();
        var columns = new List<string>();
        var body = rows;
        var headRow = rows.FirstOrDefault(r => r.Parent?.Is("thead") == true)
            ?? (rows.Count > 0 && rows[0].Elements().All(c => c.Is("th")) && rows[0].Elements().Any() ? rows[0] : null);
        if (headRow is not null)
        {
            columns.AddRange(headRow.Elements().Where(c => c.Is("th") || c.Is("td")).Select(PlainText));
            body = rows.Where(r => !ReferenceEquals(r, headRow) && r.Parent?.Is("thead") != true).ToList();
        }

        var widths = new List<double>();
        foreach (var part in (table.Attr("data-widths") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Timecode.TryParseAttribute(part, out var w) && w > 0)
            {
                widths.Add(w);
            }
        }

        return new TableBlock
        {
            Columns = columns,
            Widths = widths,
            Rows = body.Select(r => new TableRow
            {
                Cells = r.Elements().Where(c => c.Is("td") || c.Is("th")).Select(c => new TableCell { Runs = Runs(c.Children) }).ToList(),
            }).Where(r => r.Cells.Count > 0).ToList(),
        };
    }

    private static LabelValueBlock LabelValue(HtmlNode dl)
    {
        var pairs = new List<LabelValuePair>();
        string? label = null;
        foreach (var child in dl.Elements())
        {
            if (child.Is("dt"))
            {
                if (label is not null)
                {
                    pairs.Add(new LabelValuePair { Label = label });
                }

                label = PlainText(child);
            }
            else if (child.Is("dd"))
            {
                pairs.Add(new LabelValuePair { Label = label ?? string.Empty, Runs = Runs(child.Children) });
                label = null;
            }
        }

        if (label is not null)
        {
            pairs.Add(new LabelValuePair { Label = label });
        }

        return new LabelValueBlock { Pairs = pairs };
    }

    private static QuoteBlock Quote(HtmlNode blockquote)
    {
        var footer = blockquote.Elements().FirstOrDefault(e => e.Is("footer"));
        var paragraphs = blockquote.Children.Where(c => !ReferenceEquals(c, footer)).ToList();
        var runs = new List<Run>();
        foreach (var part in paragraphs.Where(p => !p.IsText || !string.IsNullOrWhiteSpace(p.Text)))
        {
            var partRuns = Runs(part.Is("p") ? part.Children : [part]);
            if (partRuns.Count == 0)
            {
                continue;
            }

            if (runs.Count > 0)
            {
                runs.Add(Run.Plain("\n"));
            }

            runs.AddRange(partRuns);
        }

        var cite = footer?.Find(n => n.Is("cite"));
        double? t = Timecode.TryParseAttribute(blockquote.Attr("data-t"), out var at) ? at : null;
        return new QuoteBlock { Runs = Merge(runs), Attribution = cite is null ? null : PlainText(cite), T = t };
    }

    private static TimelineBlock Timeline(HtmlNode list)
    {
        var entries = new List<TimelineEntry>();
        foreach (var li in list.Elements().Where(e => e.Is("li")))
        {
            var t = Seconds(li.Attr("data-t") ?? li.Find(n => n.HasClass("tl-t"))?.Attr("data-t"));
            var text = li.Elements().FirstOrDefault(e => e.HasClass("tl-x"));
            var runs = text is not null ? Runs(text.Children) : Runs(li.Children.Where(c => !c.HasClass("tl-t")));
            entries.Add(new TimelineEntry { T = t, Runs = runs });
        }

        return new TimelineBlock { Entries = entries };
    }

    private static TranscriptBlock Transcript(HtmlNode table)
    {
        var segments = new List<TranscriptLine>();
        var chapters = new List<TranscriptChapter>();
        foreach (var row in table.FindAll(n => n.Is("tr")))
        {
            var t = Seconds(row.Attr("data-t"));
            if (row.HasClass("tr-ch"))
            {
                chapters.Add(new TranscriptChapter { T = t, Title = PlainText(row) });
                continue;
            }

            var speaker = row.Find(n => n.HasClass("tr-sp"));
            var text = row.Find(n => n.HasClass("tr-x"));
            segments.Add(new TranscriptLine
            {
                Id = row.Attr("data-seg"),
                T = t,
                Speaker = speaker is null ? string.Empty : PlainText(speaker),
                Text = text is null ? string.Empty : TextOf(Runs(text.Children)),
            });
        }

        return new TranscriptBlock { Segments = segments, Chapters = chapters };
    }

    /// <summary>Inline content to runs: emphasis, notes, timestamps, line breaks; whitespace collapsed as displayed.</summary>
    private static List<Run> Runs(IEnumerable<HtmlNode> nodes)
    {
        var raw = new List<Run>();
        foreach (var node in nodes)
        {
            Collect(node, bold: false, italic: false, note: false, raw);
        }

        return Merge(Normalize(raw));
    }

    private static void Collect(HtmlNode node, bool bold, bool italic, bool note, List<Run> into)
    {
        if (node.IsText)
        {
            into.Add(Make(node.Text ?? string.Empty, bold, italic, note));
            return;
        }

        if (node.Is("br"))
        {
            into.Add(Make(LineBreak.ToString(), bold, italic, note));
            return;
        }

        if (IsTimestamp(node))
        {
            var t = Seconds(node.Attr("data-t"));
            into.Add(Run.Timestamp(t, CollapseWhitespace(node.InnerText()).Trim()));
            return;
        }

        if (node.Is("sup") && node.HasClass("fn"))
        {
            return;
        }

        var style = node.Attr("style") ?? string.Empty;
        bold |= node.Is("strong") || node.Is("b") || BoldStyle().IsMatch(style);
        italic |= node.Is("em") || node.Is("i") || ItalicStyle().IsMatch(style);
        note |= node.HasClass("note");
        var separate = BlockTags.Contains(node.Name!);
        if (separate && into.Count > 0)
        {
            into.Add(Run.Plain(" "));
        }

        foreach (var child in node.Children)
        {
            Collect(child, bold, italic, note, into);
        }

        if (separate)
        {
            into.Add(Run.Plain(" "));
        }
    }

    private static bool IsTimestamp(HtmlNode node) =>
        node.HasClass("ts") || (node.Attr("data-t") is not null && (node.Is("a") || node.Is("span")) && !node.HasClass("tl-t") && !node.HasClass("q-t"));

    private static Run Make(string text, bool bold, bool italic, bool note)
    {
        if (note)
        {
            return Run.Note(text);
        }

        return (bold, italic) switch
        {
            (true, true) => Run.BoldItalic(text),
            (true, false) => Run.Bold(text),
            (false, true) => Run.Italic(text),
            _ => Run.Plain(text),
        };
    }

    /// <summary>Collapses whitespace inside and across runs, trims the ends, and turns line-break markers into <c>\n</c>.</summary>
    private static List<Run> Normalize(List<Run> raw)
    {
        var result = new List<Run>(raw.Count);
        var lastEndsInSpace = true;
        foreach (var run in raw)
        {
            if (run.Kind == RunKind.Timestamp)
            {
                result.Add(run);
                lastEndsInSpace = false;
                continue;
            }

            var text = CollapseWhitespace(run.Text);
            if (lastEndsInSpace)
            {
                text = text.TrimStart(' ');
            }

            text = text.Replace(LineBreak + " ", LineBreak.ToString(), StringComparison.Ordinal);
            if (text.Length == 0)
            {
                continue;
            }

            lastEndsInSpace = text[^1] is ' ' or LineBreak;
            result.Add(run with { Text = text });
        }

        // Trim trailing whitespace and spaces before line breaks.
        for (var k = result.Count - 1; k >= 0; k--)
        {
            if (result[k].Kind == RunKind.Timestamp)
            {
                break;
            }

            var trimmed = result[k].Text.TrimEnd(' ', LineBreak);
            if (trimmed.Length > 0)
            {
                result[k] = result[k] with { Text = trimmed };
                break;
            }

            result.RemoveAt(k);
        }

        for (var k = 0; k < result.Count; k++)
        {
            if (result[k].Kind != RunKind.Timestamp)
            {
                var text = result[k].Text.Replace(" " + LineBreak, LineBreak.ToString(), StringComparison.Ordinal).Replace(LineBreak, '\n');
                result[k] = result[k] with { Text = text };
            }
        }

        return result.Where(r => r.Kind == RunKind.Timestamp || r.Text.Length > 0).ToList();
    }

    /// <summary>Joins neighbouring runs of the same kind and emphasis.</summary>
    private static List<Run> Merge(List<Run> runs)
    {
        var result = new List<Run>(runs.Count);
        foreach (var run in runs)
        {
            if (result.Count > 0 && run.Kind != RunKind.Timestamp && result[^1].Kind == run.Kind && result[^1].Style == run.Style && result[^1].ExtensionData is null && run.ExtensionData is null)
            {
                result[^1] = result[^1] with { Text = result[^1].Text + run.Text };
            }
            else
            {
                result.Add(run);
            }
        }

        return result;
    }

    private static string CollapseWhitespace(string text)
    {
        var builder = new StringBuilder(text.Length);
        var space = false;
        foreach (var c in text)
        {
            if (c == LineBreak)
            {
                builder.Append(c);
                space = false;
            }
            else if (char.IsWhiteSpace(c))
            {
                // Includes the no-break space that contenteditable inserts for trailing spaces.
                if (!space)
                {
                    builder.Append(' ');
                    space = true;
                }
            }
            else
            {
                builder.Append(c);
                space = false;
            }
        }

        return builder.ToString();
    }

    private static double Seconds(string? value) => Timecode.TryParseAttribute(value, out var seconds) ? seconds : 0;

    private static string PlainText(HtmlNode node) => CollapseWhitespace(node.InnerText().Replace('\n', ' ')).Trim();

    private static string TextOf(IEnumerable<Run> runs) => string.Concat(runs.Select(r => r.Text));

    [GeneratedRegex(@"font-weight\s*:\s*(bold|[6-9]00)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BoldStyle();

    [GeneratedRegex(@"font-style\s*:\s*italic", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ItalicStyle();
}
