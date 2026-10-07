namespace Memento.Documents.Agenda.Layout;

/// <summary>Rebuilds text lines from word boxes: words whose boxes overlap vertically by half their height share a line.</summary>
internal static class LineBuilder
{
    public static List<LineBox> Build(IEnumerable<WordBox> words)
    {
        var sorted = words.Where(w => w.Text.Trim().Length > 0 && w.Height > 0).OrderBy(w => w.CenterY).ThenBy(w => w.Left).ToList();
        var lines = new List<Building>();
        foreach (var word in sorted)
        {
            Building? best = null;
            var bestRatio = 0.0;
            for (var i = lines.Count - 1; i >= Math.Max(0, lines.Count - 6); i--)
            {
                var line = lines[i];
                var overlap = Math.Min(word.Bottom, line.Bottom) - Math.Max(word.Top, line.Top);
                var ratio = overlap / Math.Min(word.Height, line.Height);
                if (ratio >= 0.5 && ratio > bestRatio && !line.Collides(word))
                {
                    best = line;
                    bestRatio = ratio;
                }
            }

            if (best is null)
            {
                lines.Add(new Building(word));
            }
            else
            {
                best.Add(word);
            }
        }

        return lines
            .Select(l => new LineBox(l.Words.OrderBy(w => w.Left).ToList()))
            .OrderBy(l => LineBox.Median(l.Words.Select(w => w.CenterY)))
            .ThenBy(l => l.Left)
            .ToList();
    }

    private sealed class Building
    {
        public Building(WordBox first)
        {
            Words.Add(first);
            Top = first.Top;
            Bottom = first.Bottom;
        }

        public List<WordBox> Words { get; } = [];

        public double Top { get; private set; }

        public double Bottom { get; private set; }

        /// <summary>The typical word height, so one tall glyph does not stretch the line over its neighbours.</summary>
        public double Height => LineBox.Median(Words.Select(w => w.Height));

        public void Add(WordBox word)
        {
            Words.Add(word);

            // Track the line by the median top and bottom of its words, so a slightly skewed line still collects its words.
            Top = LineBox.Median(Words.Select(w => w.Top));
            Bottom = LineBox.Median(Words.Select(w => w.Bottom));
        }

        public bool Collides(WordBox word) =>
            Words.Any(w => Math.Min(w.Right, word.Right) - Math.Max(w.Left, word.Left) > 0.5 * Math.Min(w.Width, word.Width));
    }
}
