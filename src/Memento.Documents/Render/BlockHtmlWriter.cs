using System.Text;
using Memento.Documents.Model;
using Memento.Documents.Model.Blocks;

namespace Memento.Documents.Render;

/// <summary>Writes blocks and runs as paper markup. In <see cref="PaperMode.Print"/> timestamps become numbered footnote references.</summary>
internal sealed class BlockHtmlWriter
{
    private readonly StringBuilder _out;
    private readonly PaperMode _mode;
    private readonly List<Footnote> _footnotes = [];

    public BlockHtmlWriter(StringBuilder output, PaperMode mode)
    {
        _out = output;
        _mode = mode;
    }

    public IReadOnlyList<Footnote> Footnotes => _footnotes;

    private bool Interactive => _mode is PaperMode.Viewer or PaperMode.Sample;

    private string ReadOnly => _mode == PaperMode.Viewer ? " contenteditable=\"false\"" : string.Empty;

    public void Write(Block block)
    {
        switch (block)
        {
            case HeadingBlock heading:
                var level = Math.Clamp(heading.Level, 1, 3) + 2;
                _out.Append("<h").Append(level).Append(" class=\"paper-sub\">");
                Runs(heading.Runs);
                _out.Append("</h").Append(level).Append(">\n");
                break;
            case ParagraphBlock paragraph:
                _out.Append("<p>");
                Runs(paragraph.Runs);
                _out.Append("</p>\n");
                break;
            case ListBlock list:
                List(list.Style, list.Items);
                _out.Append('\n');
                break;
            case TableBlock table:
                Table(table);
                break;
            case ChipsBlock chips:
                _out.Append("<div class=\"chips\"").Append(ReadOnly).Append('>');
                foreach (var item in chips.Items)
                {
                    _out.Append("<span class=\"chip\">").Append(HtmlText.Escape(item)).Append("</span>");
                }

                _out.Append("</div>\n");
                break;
            case LabelValueBlock pairs:
                _out.Append("<dl class=\"kv\">");
                foreach (var pair in pairs.Pairs)
                {
                    _out.Append("<dt>").Append(HtmlText.Escape(pair.Label)).Append("</dt><dd>");
                    Runs(pair.Runs);
                    _out.Append("</dd>");
                }

                _out.Append("</dl>\n");
                break;
            case QuoteBlock quote:
                Quote(quote);
                break;
            case TimelineBlock timeline:
                Timeline(timeline);
                break;
            case TranscriptBlock transcript:
                Transcript(transcript);
                break;
        }
    }

    public void Runs(IEnumerable<Run> runs)
    {
        foreach (var run in runs)
        {
            switch (run.Kind)
            {
                case RunKind.Timestamp when run.T is { } t:
                    Timestamp(t, run.Text);
                    break;
                case RunKind.Emphasis:
                    var open = run.Style switch
                    {
                        EmphasisStyle.Italic => "<em>",
                        EmphasisStyle.BoldItalic => "<strong><em>",
                        _ => "<strong>",
                    };
                    var close = run.Style switch
                    {
                        EmphasisStyle.Italic => "</em>",
                        EmphasisStyle.BoldItalic => "</em></strong>",
                        _ => "</strong>",
                    };
                    _out.Append(open).Append(HtmlText.EscapeWithBreaks(run.Text)).Append(close);
                    break;
                case RunKind.Note:
                    _out.Append("<span class=\"note\">").Append(HtmlText.EscapeWithBreaks(run.Text)).Append("</span>");
                    break;
                default:
                    _out.Append(HtmlText.EscapeWithBreaks(run.Text));
                    break;
            }
        }
    }

    /// <summary>The print footnotes section; empty when there are none.</summary>
    public void FootnoteSection()
    {
        if (_footnotes.Count == 0)
        {
            return;
        }

        _out.Append("<section class=\"paper-footnotes\" aria-label=\"Timestamps\"><ol>");
        foreach (var note in _footnotes)
        {
            _out.Append("<li id=\"fn-").Append(note.Number).Append("\" data-t=\"").Append(Timecode.ToAttribute(note.T)).Append("\">")
                .Append(HtmlText.Escape(note.Text)).Append("</li>");
        }

        _out.Append("</ol></section>\n");
    }

    private void Timestamp(double t, string display)
    {
        var text = string.IsNullOrWhiteSpace(display) ? Timecode.Format(t) : display;
        if (_mode == PaperMode.Print)
        {
            var number = _footnotes.Count + 1;
            _footnotes.Add(new Footnote(number, t, text, Timecode.FootnoteText(t, text)));

            // A generated sentence ends "… gatherings. [0:49]"; on paper the marker sits on the word before it, so the
            // space between them goes (otherwise the line can break there and leave the marker alone on the next line).
            if (_out.Length > 0 && _out[^1] == ' ')
            {
                _out.Length--;
            }

            _out.Append("<sup class=\"fn\" id=\"fnref-").Append(number).Append("\"><a href=\"#fn-").Append(number).Append("\">")
                .Append(number).Append("</a></sup>");
            return;
        }

        _out.Append("<a class=\"ts\" href=\"#t=").Append(Timecode.ToAttribute(t)).Append("\" data-t=\"").Append(Timecode.ToAttribute(t)).Append('"')
            .Append(ReadOnly).Append('>').Append(HtmlText.Escape(text)).Append("</a>");
    }

    private void List(ListStyle style, IReadOnlyList<ListItem> items)
    {
        var tag = style == ListStyle.Numbered ? "ol" : "ul";
        _out.Append('<').Append(tag).Append('>');
        foreach (var item in items)
        {
            _out.Append("<li>");
            Runs(item.Runs);
            if (item.Items.Count > 0)
            {
                List(style, item.Items);
            }

            _out.Append("</li>");
        }

        _out.Append("</").Append(tag).Append('>');
    }

    private void Table(TableBlock table)
    {
        _out.Append("<table class=\"paper-table\"");
        var widths = table.Widths.Count > 0 && table.Widths.All(w => w > 0) ? table.Widths : null;
        if (widths is not null)
        {
            _out.Append(" data-widths=\"").Append(string.Join(",", widths.Select(HtmlText.Number))).Append('"');
        }

        _out.Append('>');
        if (widths is not null)
        {
            var total = widths.Sum();
            _out.Append("<colgroup>");
            foreach (var width in widths)
            {
                _out.Append("<col style=\"width:").Append(HtmlText.Number(width / total * 100)).Append("%\">");
            }

            _out.Append("</colgroup>");
        }

        if (table.Columns.Count > 0)
        {
            _out.Append("<thead><tr>");
            foreach (var column in table.Columns)
            {
                _out.Append("<th>").Append(HtmlText.Escape(column)).Append("</th>");
            }

            _out.Append("</tr></thead>");
        }

        _out.Append("<tbody>");
        foreach (var row in table.Rows)
        {
            _out.Append("<tr>");
            foreach (var cell in row.Cells)
            {
                _out.Append("<td>");
                Runs(cell.Runs);
                _out.Append("</td>");
            }

            _out.Append("</tr>");
        }

        _out.Append("</tbody></table>\n");
    }

    private void Quote(QuoteBlock quote)
    {
        _out.Append("<blockquote class=\"paper-quote\"");
        if (quote.T is { } at)
        {
            _out.Append(" data-t=\"").Append(Timecode.ToAttribute(at)).Append('"');
        }

        _out.Append("><p>");
        Runs(quote.Runs);
        _out.Append("</p>");
        if (!string.IsNullOrWhiteSpace(quote.Attribution) || quote.T is not null)
        {
            _out.Append("<footer").Append(ReadOnly).Append(">— ");
            if (!string.IsNullOrWhiteSpace(quote.Attribution))
            {
                _out.Append("<cite>").Append(HtmlText.Escape(quote.Attribution)).Append("</cite>");
            }

            if (quote.T is { } t)
            {
                if (!string.IsNullOrWhiteSpace(quote.Attribution))
                {
                    _out.Append(MetaLine.Separator);
                }

                TimeLabel(t, "q-t");
            }

            _out.Append("</footer>");
        }

        _out.Append("</blockquote>\n");
    }

    private void Timeline(TimelineBlock timeline)
    {
        _out.Append("<ol class=\"timeline\">");
        foreach (var entry in timeline.Entries)
        {
            _out.Append("<li data-t=\"").Append(Timecode.ToAttribute(entry.T)).Append("\">");
            TimeLabel(entry.T, "tl-t");
            _out.Append("<span class=\"tl-x\">");
            Runs(entry.Runs);
            _out.Append("</span></li>");
        }

        _out.Append("</ol>\n");
    }

    /// <summary>A visible time: a link to the moment on screen, plain text in print.</summary>
    private void TimeLabel(double t, string className)
    {
        if (Interactive)
        {
            _out.Append("<a class=\"").Append(className).Append("\" href=\"#t=").Append(Timecode.ToAttribute(t)).Append("\" data-t=\"")
                .Append(Timecode.ToAttribute(t)).Append('"').Append(ReadOnly).Append('>').Append(Timecode.Format(t)).Append("</a>");
        }
        else
        {
            _out.Append("<span class=\"").Append(className).Append("\">").Append(Timecode.Format(t)).Append("</span>");
        }
    }

    private void Transcript(TranscriptBlock transcript)
    {
        _out.Append("<table class=\"paper-transcript\"").Append(ReadOnly).Append("><tbody>");
        foreach (var item in transcript.Interleaved())
        {
            if (item is TranscriptChapter chapter)
            {
                _out.Append("<tr class=\"tr-ch\" data-t=\"").Append(Timecode.ToAttribute(chapter.T)).Append("\"><th colspan=\"3\">")
                    .Append(HtmlText.Escape(chapter.Title)).Append("</th></tr>");
            }
            else if (item is TranscriptLine line)
            {
                _out.Append("<tr data-t=\"").Append(Timecode.ToAttribute(line.T)).Append('"');
                if (!string.IsNullOrEmpty(line.Id))
                {
                    _out.Append(" data-seg=\"").Append(HtmlText.Escape(line.Id)).Append('"');
                }

                _out.Append("><td class=\"tr-t\">").Append(Timecode.Format(line.T)).Append("</td><td class=\"tr-sp\">")
                    .Append(HtmlText.Escape(line.Speaker)).Append("</td><td class=\"tr-x\">").Append(HtmlText.EscapeWithBreaks(line.Text)).Append("</td></tr>");
            }
        }

        _out.Append("</tbody></table>\n");
    }
}
