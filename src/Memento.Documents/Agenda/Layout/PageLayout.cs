using Memento.Documents.Agenda.Text;

namespace Memento.Documents.Agenda.Layout;

/// <summary>
/// Puts a page's lines in reading order. A page with two text columns (a clear vertical gap in the middle with text
/// on both sides) is read left column first, then right, in bands separated by lines that span both columns.
/// A right-hand column of short entries on the same baselines (a presenter or time column) is a table, not a column.
/// </summary>
internal static class PageLayout
{
    private const int Bins = 240;

    /// <summary>The lines in reading order, each with its column: 0 for a single-column page or a spanning line, 1 left, 2 right.</summary>
    public static (List<(LineBox Line, int Column)> Lines, bool TwoColumns) ReadingOrder(IReadOnlyList<WordBox> words)
    {
        var lines = LineBuilder.Build(words);
        var single = lines.Select(l => (l, 0)).ToList();
        if (words.Count < 8 || lines.Count < 4 || FindGap(words) is not var (gapStart, gapEnd))
        {
            return (single, false);
        }

        // A line with a word in the middle half of the gap spans both columns (a centred title); the gap's ragged
        // edges belong to the columns.
        var quarter = (gapEnd - gapStart) / 4;
        bool Spans(LineBox line) => line.Words.Any(w => w.Right > gapStart + quarter && w.Left < gapEnd - quarter);
        var spanningCount = lines.Count(Spans);
        if (spanningCount > Math.Max(2, lines.Count / 10))
        {
            return (single, false);
        }

        // Split the remaining words at the middle of the truly empty space between the columns.
        var middle = (gapStart + gapEnd) / 2;
        var columnWords = lines.Where(l => !Spans(l)).SelectMany(l => l.Words).ToList();
        var leftEdge = columnWords.Where(w => Center(w) < middle).Select(w => w.Right).DefaultIfEmpty(gapStart).Max();
        var rightEdge = columnWords.Where(w => Center(w) >= middle).Select(w => w.Left).DefaultIfEmpty(gapEnd).Min();
        var splitX = (leftEdge + rightEdge) / 2;

        var bands = new List<(List<LineBox> Spanning, List<LineBox> Left, List<LineBox> Right)> { ([], [], []) };
        foreach (var line in lines)
        {
            if (Spans(line))
            {
                if (bands[^1].Left.Count > 0 || bands[^1].Right.Count > 0)
                {
                    bands.Add(([], [], []));
                }

                bands[^1].Spanning.Add(line);
                continue;
            }

            var left = line.Words.Where(w => Center(w) < splitX).ToList();
            var right = line.Words.Where(w => Center(w) >= splitX).ToList();
            if (left.Count > 0)
            {
                bands[^1].Left.Add(new LineBox(left));
            }

            if (right.Count > 0)
            {
                bands[^1].Right.Add(new LineBox(right));
            }
        }

        var leftLines = bands.SelectMany(b => b.Left).ToList();
        var rightLines = bands.SelectMany(b => b.Right).ToList();
        if (leftLines.Count < 3 || rightLines.Count < 3 || LooksLikeTable(leftLines, rightLines))
        {
            return (single, false);
        }

        var ordered = new List<(LineBox, int)>();
        foreach (var (spanning, left, right) in bands)
        {
            ordered.AddRange(spanning.Select(l => (l, 0)));
            ordered.AddRange(left.Select(l => (l, 1)));
            ordered.AddRange(right.Select(l => (l, 2)));
        }

        return (ordered, true);
    }

    private static double Center(WordBox word) => word.Left + (word.Width / 2);

    /// <summary>The widest run of nearly empty space near the middle of the text; a title across both columns may cross it.</summary>
    private static (double Start, double End)? FindGap(IReadOnlyList<WordBox> words)
    {
        var minX = words.Min(w => w.Left);
        var maxX = words.Max(w => w.Right);
        var extent = maxX - minX;
        if (extent <= 0)
        {
            return null;
        }

        var coverage = new int[Bins];
        foreach (var word in words)
        {
            var from = Math.Clamp((int)((word.Left - minX) / extent * Bins), 0, Bins - 1);
            var to = Math.Clamp((int)((word.Right - minX) / extent * Bins), 0, Bins - 1);
            for (var b = from; b <= to; b++)
            {
                coverage[b]++;
            }
        }

        var medianHeight = LineBox.Median(words.Select(w => w.Height));
        var minimumWidth = Math.Max(0.025 * extent, 1.5 * medianHeight);
        int bestStart = -1, bestLength = 0, start = -1;
        for (var b = 0; b <= Bins; b++)
        {
            var empty = b < Bins && coverage[b] <= 2;
            if (empty && start < 0)
            {
                start = b;
            }
            else if (!empty && start >= 0)
            {
                var length = b - start;
                var center = (start + (length / 2.0)) / Bins;
                if (center is >= 0.3 and <= 0.7 && length > bestLength && length * extent / Bins >= minimumWidth)
                {
                    bestStart = start;
                    bestLength = length;
                }

                start = -1;
            }
        }

        if (bestStart < 0)
        {
            return null;
        }

        var gapStart = minX + (bestStart / (double)Bins * extent);
        var gapEnd = minX + ((bestStart + bestLength) / (double)Bins * extent);
        var leftCount = words.Count(w => Center(w) < gapStart);
        var rightCount = words.Count(w => Center(w) > gapEnd);
        return leftCount * 100 >= words.Count * 15 && rightCount * 100 >= words.Count * 15 ? (gapStart, gapEnd) : null;
    }

    private static bool LooksLikeTable(List<LineBox> left, List<LineBox> right)
    {
        // A right-hand list of its own (bullets, numbers, times) is a column.
        var rightMarked = right.Count(r => MarkerParser.TryParseMarker(r.Text, out _, out _) || MarkerParser.TryParseTimePrefix(r.Text, out _, out _));
        if (rightMarked * 2 >= right.Count)
        {
            return false;
        }

        // Right-hand entries much shorter than the left ones, on the left lines' baselines: "Welcome …… A. Chair".
        var aligned = right.Count(r => left.Any(l => Math.Abs(((l.Top + l.Bottom) / 2) - ((r.Top + r.Bottom) / 2)) < 0.3 * Math.Max(l.Height, r.Height)));
        var leftWords = left.Average(l => l.Words.Count);
        var rightWords = right.Average(r => r.Words.Count);
        var shortEntries = (rightWords <= 3 && leftWords >= 1.5 * rightWords) || leftWords <= 2;
        return shortEntries && aligned * 10 >= right.Count * 8;
    }
}
