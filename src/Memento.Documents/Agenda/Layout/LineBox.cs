namespace Memento.Documents.Agenda.Layout;

/// <summary>A line rebuilt from word boxes: its words left to right and the union of their boxes.</summary>
internal sealed record LineBox(IReadOnlyList<WordBox> Words)
{
    public double Left => Words.Min(w => w.Left);

    public double Right => Words.Max(w => w.Right);

    public double Top => Words.Min(w => w.Top);

    public double Bottom => Words.Max(w => w.Bottom);

    /// <summary>The median word height: the line's text size, robust to a tall bracket or a short dash.</summary>
    public double Height => Median(Words.Select(w => w.Height));

    public double? FontSize => Words.All(w => w.FontSize is not null) ? Median(Words.Select(w => w.FontSize!.Value)) : null;

    public string Text => string.Join(" ", Words.Select(w => w.Text));

    internal static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToList();
        if (sorted.Count == 0)
        {
            return 0;
        }

        var middle = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }
}
