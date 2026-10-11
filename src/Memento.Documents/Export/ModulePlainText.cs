using System.Text;
using Memento.Documents.Model;
using Memento.Documents.Model.Blocks;

namespace Memento.Documents.Export;

/// <summary>
/// One module as plain text (2.0, <c>documents.copyModule</c>): what a mail program or a chat box shows when the copy
/// is pasted as text. No Markdown marks: labels read "To: …", lists use "- ", tables one row per line with " · "
/// between cells; emphasis is plain; timestamps are left out (they point into the recording, which the readers of an
/// email do not have). Line endings are <c>\r\n</c>, as Windows programs expect from the clipboard.
/// </summary>
public static partial class ModulePlainText
{
    public static string Write(ModuleBlock module)
    {
        ArgumentNullException.ThrowIfNull(module);
        var lines = new List<string>();
        foreach (var block in module.Blocks)
        {
            if (lines.Count > 0)
            {
                lines.Add(string.Empty);
            }

            Block(lines, block);
        }

        return string.Join("\r\n", lines).TrimEnd() + "\r\n";
    }

    private static void Block(List<string> lines, Block block)
    {
        switch (block)
        {
            case HeadingBlock heading:
                lines.Add(Runs(heading.Runs));
                break;
            case ParagraphBlock paragraph:
                lines.AddRange(Runs(paragraph.Runs).Split('\n'));
                break;
            case ListBlock list:
                Items(lines, list.Style, list.Items, 0);
                break;
            case TableBlock table:
                if (table.Columns.Count > 0)
                {
                    lines.Add(string.Join(" · ", table.Columns));
                }

                lines.AddRange(table.Rows.Select(r => string.Join(" · ", r.Cells.Select(c => Runs(c.Runs)))));
                break;
            case ChipsBlock chips:
                lines.Add(string.Join(", ", chips.Items));
                break;
            case LabelValueBlock pairs:
                lines.AddRange(pairs.Pairs.Select(p => $"{p.Label}: {Runs(p.Runs)}"));
                break;
            case QuoteBlock quote:
                lines.Add("“" + Runs(quote.Runs) + "”" + (string.IsNullOrWhiteSpace(quote.Attribution) ? string.Empty : " — " + quote.Attribution));
                break;
            case TimelineBlock timeline:
                lines.AddRange(timeline.Entries.Select(e => $"{Timecode.Format(e.T)} {Runs(e.Runs)}"));
                break;
            case TranscriptBlock transcript:
                foreach (var item in transcript.Interleaved())
                {
                    switch (item)
                    {
                        case TranscriptChapter chapter:
                            lines.Add(chapter.Title);
                            break;
                        case TranscriptLine line:
                            lines.Add($"{line.Speaker} [{Timecode.Format(line.T)}] {line.Text}");
                            break;
                    }
                }

                break;
        }
    }

    private static void Items(List<string> lines, ListStyle style, IReadOnlyList<ListItem> items, int depth)
    {
        var number = 1;
        foreach (var item in items)
        {
            var marker = style == ListStyle.Numbered ? $"{number++}." : "-";
            lines.Add(new string(' ', depth * 2) + marker + " " + Runs(item.Runs).Replace("\n", " ", StringComparison.Ordinal));
            Items(lines, style, item.Items, depth + 1);
        }
    }

    private static string Runs(IEnumerable<Run> runs)
    {
        var text = new StringBuilder();
        foreach (var run in runs.Where(r => r.Kind != RunKind.Timestamp))
        {
            text.Append(run.Text);
        }

        // A timestamp leaves the space before it behind: "agreed. " then nothing.
        return MultipleSpaces().Replace(text.ToString(), " ").Replace(" .", ".", StringComparison.Ordinal).Trim();
    }

    [System.Text.RegularExpressions.GeneratedRegex(" {2,}", System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex MultipleSpaces();
}
