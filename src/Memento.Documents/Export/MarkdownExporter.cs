using System.Text;
using Memento.Documents.Model;
using Memento.Documents.Model.Blocks;
using Memento.Documents.Model.Modules;

namespace Memento.Documents.Export;

/// <summary>
/// Markdown export: styles are ignored, side-by-side modules are stacked in reading order, timestamps are inline
/// <c>[18:42]</c> markers, tables are pipe tables and the Full transcript is emitted in full. Line endings are <c>\n</c>.
/// </summary>
public sealed class MarkdownExporter
{
    private readonly ModuleCatalog _catalog;

    public MarkdownExporter(ModuleCatalog? catalog = null)
    {
        _catalog = catalog ?? ModuleCatalog.Default;
    }

    public string Export(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var md = new StringBuilder();
        md.Append("# ").Append(Escape(document.Title)).Append("\n\n");
        var meta = MetaLine.Format(document.Meta);
        if (meta.Length > 0)
        {
            md.Append(Escape(meta)).Append("\n\n");
        }

        foreach (var module in document.Modules())
        {
            var title = string.IsNullOrWhiteSpace(module.Title) ? _catalog.Find(module.Type)?.DisplayName ?? string.Empty : module.Title;
            md.Append("## ").Append(Escape(title)).Append("\n\n");
            foreach (var block in module.Blocks)
            {
                Block(md, block);
            }
        }

        return md.ToString().TrimEnd('\n') + "\n";
    }

    private static void Block(StringBuilder md, Block block)
    {
        switch (block)
        {
            case HeadingBlock heading:
                md.Append(new string('#', Math.Clamp(heading.Level, 1, 3) + 2)).Append(' ').Append(Runs(heading.Runs)).Append("\n\n");
                break;
            case ParagraphBlock paragraph:
                md.Append(Runs(paragraph.Runs)).Append("\n\n");
                break;
            case ListBlock list:
                Items(md, list.Style, list.Items, 0);
                md.Append('\n');
                break;
            case TableBlock table:
                Table(md, table);
                break;
            case ChipsBlock chips:
                md.Append(string.Join(MetaLine.Separator, chips.Items.Select(Escape))).Append("\n\n");
                break;
            case LabelValueBlock pairs:
                foreach (var pair in pairs.Pairs)
                {
                    md.Append("**").Append(Escape(pair.Label)).Append(":** ").Append(Runs(pair.Runs)).Append("  \n");
                }

                md.Length -= 3;
                md.Append("\n\n");
                break;
            case QuoteBlock quote:
                foreach (var line in Runs(quote.Runs).Split('\n'))
                {
                    md.Append("> ").Append(line).Append('\n');
                }

                var attribution = new List<string>();
                if (!string.IsNullOrWhiteSpace(quote.Attribution))
                {
                    attribution.Add(Escape(quote.Attribution));
                }

                if (quote.T is { } t)
                {
                    attribution.Add("[" + Timecode.Format(t) + "]");
                }

                if (attribution.Count > 0)
                {
                    md.Append(">\n> — ").Append(string.Join(" ", attribution)).Append('\n');
                }

                md.Append('\n');
                break;
            case TimelineBlock timeline:
                foreach (var entry in timeline.Entries)
                {
                    md.Append("- [").Append(Timecode.Format(entry.T)).Append("] ").Append(Runs(entry.Runs)).Append('\n');
                }

                md.Append('\n');
                break;
            case TranscriptBlock transcript:
                foreach (var item in transcript.Interleaved())
                {
                    if (item is TranscriptChapter chapter)
                    {
                        md.Append("### ").Append(Escape(chapter.Title)).Append("\n\n");
                    }
                    else if (item is TranscriptLine line)
                    {
                        md.Append("**").Append(Escape(line.Speaker)).Append("** [").Append(Timecode.Format(line.T)).Append("] ")
                            .Append(Escape(line.Text).Replace("\n", "  \n", StringComparison.Ordinal)).Append("\n\n");
                    }
                }

                break;
        }
    }

    private static void Items(StringBuilder md, ListStyle style, IReadOnlyList<ListItem> items, int depth)
    {
        var number = 1;
        foreach (var item in items)
        {
            var marker = style == ListStyle.Numbered ? $"{number++}." : "-";
            var indent = new string(' ', depth * (style == ListStyle.Numbered ? 3 : 2));
            md.Append(indent).Append(marker).Append(' ').Append(Runs(item.Runs).Replace("\n", "  \n" + indent + "   ", StringComparison.Ordinal)).Append('\n');
            Items(md, style, item.Items, depth + 1);
        }
    }

    private static void Table(StringBuilder md, TableBlock table)
    {
        var columns = Math.Max(table.Columns.Count, table.Rows.Count == 0 ? 0 : table.Rows.Max(r => r.Cells.Count));
        if (columns == 0)
        {
            return;
        }

        var header = Enumerable.Range(0, columns).Select(c => c < table.Columns.Count ? Cell(Escape(table.Columns[c])) : " ");
        md.Append("| ").Append(string.Join(" | ", header)).Append(" |\n");
        md.Append('|').Append(string.Concat(Enumerable.Repeat(" --- |", columns))).Append('\n');
        foreach (var row in table.Rows)
        {
            var cells = Enumerable.Range(0, columns).Select(c => c < row.Cells.Count ? Cell(Runs(row.Cells[c].Runs)) : " ");
            md.Append("| ").Append(string.Join(" | ", cells)).Append(" |\n");
        }

        md.Append('\n');
    }

    private static string Cell(string text)
    {
        var value = text.Replace("|", "\\|", StringComparison.Ordinal).Replace("\n", "<br>", StringComparison.Ordinal);
        return value.Length == 0 ? " " : value;
    }

    /// <summary>Runs as Markdown: <c>**bold**</c>, <c>*italic*</c>, timestamps as <c>[18:42]</c> after a space.</summary>
    private static string Runs(IEnumerable<Run> runs)
    {
        var text = new StringBuilder();
        var afterTimestamp = false;
        foreach (var run in runs)
        {
            switch (run.Kind)
            {
                case RunKind.Timestamp when run.T is { } t:
                    if (text.Length > 0 && !char.IsWhiteSpace(text[^1]))
                    {
                        text.Append(' ');
                    }

                    // The label comes from edited markup: escaped, on one line, so "](javascript:…)" or "]\n# x" stays text.
                    var label = string.IsNullOrWhiteSpace(run.Text) ? Timecode.Format(t) : Escape(OneLine(run.Text));
                    text.Append('[').Append(label).Append(']');
                    break;
                case RunKind.Emphasis:
                    var marker = run.Style switch { EmphasisStyle.Italic => "*", EmphasisStyle.BoldItalic => "***", _ => "**" };
                    var (lead, core, trail) = SplitSpaces(Escape(run.Text));
                    text.Append(lead);
                    if (core.Length > 0)
                    {
                        text.Append(marker).Append(core).Append(marker);
                    }

                    text.Append(trail);
                    break;
                default:
                    var plain = Escape(run.Text);

                    // Right after a timestamp's "]", a "(" would make it a link and a ":" a link definition.
                    if (afterTimestamp && plain.Length > 0 && plain[0] is '(' or ':')
                    {
                        text.Append('\\');
                    }

                    text.Append(plain);
                    break;
            }

            afterTimestamp = run.Kind == RunKind.Timestamp && run.T is not null;
        }

        return text.ToString();
    }

    private static string OneLine(string value) =>
        string.Join(' ', value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    /// <summary>Emphasis markers must hug the text: "**bold** " not "**bold **".</summary>
    private static (string Lead, string Core, string Trail) SplitSpaces(string value)
    {
        var start = value.Length - value.TrimStart().Length;
        var end = value.TrimEnd().Length;
        return start >= end ? (value, string.Empty, string.Empty) : (value[..start], value[start..end], value[end..]);
    }

    /// <summary>Escapes the characters that would start Markdown syntax inside text.</summary>
    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (c is '\\' or '*' or '_' or '`' or '[' or ']' or '<' or '>' or '#')
            {
                builder.Append('\\');
            }

            builder.Append(c);
        }

        return builder.ToString();
    }
}
