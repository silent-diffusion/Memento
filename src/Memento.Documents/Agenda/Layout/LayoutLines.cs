using Memento.Documents.Agenda.Text;

namespace Memento.Documents.Agenda.Layout;

/// <summary>
/// Turns positioned lines (a PDF page, an OCR result) into <see cref="SourceLine"/>s: indentation from the left edge of
/// each column, blank lines at paragraph gaps, headings from a larger or bold font, and a heading run into the next
/// item when the font changes part-way along a line.
/// </summary>
internal static class LayoutLines
{
    public static List<SourceLine> ToSourceLines(
        IReadOnlyList<(LineBox Line, int Column)> lines,
        int? page,
        bool fromOcr,
        Func<LineBox, IReadOnlyList<string>>? reasons = null)
    {
        var result = new List<SourceLine>(lines.Count + 8);
        if (lines.Count == 0)
        {
            return result;
        }

        var bodySize = LineBox.Median(lines.Select(l => Size(l.Line)));
        var charWidth = Math.Max(0.5, bodySize * 0.5);
        var columnLeft = lines
            .GroupBy(l => l.Column)
            .ToDictionary(g => g.Key, g => g.Min(l => l.Line.Left));
        var headingRatio = fromOcr ? 1.35 : 1.25;

        LineBox? previous = null;
        var previousColumn = -1;
        var lineNumber = 0;
        foreach (var (line, column) in lines)
        {
            lineNumber++;
            var location = new AgendaSourceLocation { Page = page, Line = lineNumber };
            if (previous is not null && column == previousColumn && line.Top - previous.Bottom > 1.0 * Math.Max(Size(previous), bodySize))
            {
                result.Add(SourceLine.Blank(location));
            }
            else if (previous is not null && column != previousColumn)
            {
                result.Add(SourceLine.Blank(location));
            }

            previous = line;
            previousColumn = column;

            var text = line.Text;
            var offset = line.Left - columnLeft[column];
            var indent = (int)Math.Round(offset / (2 * charWidth)) * 4;
            var marked = MarkerParser.TryParseMarker(text, out _, out _) || MarkerParser.TryParseTimePrefix(text, out _, out _);
            var size = Size(line);
            int? heading = null;
            if (!marked && text.Length <= 80)
            {
                if (size >= headingRatio * bodySize)
                {
                    heading = 1;
                }
                else if (!fromOcr && (size >= 1.1 * bodySize || line.Words.All(w => w.Bold)) && !lines.All(l => l.Line.Words.All(w => w.Bold)))
                {
                    heading = 2;
                }
            }

            result.Add(new SourceLine(text, location)
            {
                Indent = Math.Max(0, indent),
                HeadingLevel = heading,
                HeadingMergeSuspected = heading is null && FontChangesMidLine(line, bodySize),
                Reasons = reasons?.Invoke(line) ?? [],
            });
        }

        return result;
    }

    private static double Size(LineBox line) => line.FontSize ?? line.Height;

    /// <summary>A bold or larger run at the start of the line followed by two or more regular words: "Opening Welcome from the chair".</summary>
    private static bool FontChangesMidLine(LineBox line, double bodySize)
    {
        var words = line.Words;
        if (words.Count < 3 || words.Any(w => w.FontSize is null))
        {
            return false;
        }

        var start = 0;
        while (start < words.Count && IsEmphasised(words[start], bodySize))
        {
            start++;
        }

        if (start == 0 || words.Count - start < 2)
        {
            return false;
        }

        return words.Skip(start).All(w => !IsEmphasised(w, bodySize));
    }

    private static bool IsEmphasised(WordBox word, double bodySize) => word.Bold || word.FontSize >= 1.15 * bodySize;
}
