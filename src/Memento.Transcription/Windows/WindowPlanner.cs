namespace Memento.Transcription.Windows;

/// <summary>
/// Splits a long track into windows of <c>window</c> seconds that overlap by <c>overlap</c> seconds (10 minutes and 5 s
/// by default), so progress can be reported, cancelling is prompt, and a failure keeps the finished windows.
/// </summary>
public static class WindowPlanner
{
    public const double DefaultWindowSeconds = 600;
    public const double DefaultOverlapSeconds = 5;

    public static IReadOnlyList<AudioWindow> Plan(double durationSeconds, double window = DefaultWindowSeconds, double overlap = DefaultOverlapSeconds)
    {
        if (window <= 0 || overlap < 0 || overlap >= window)
        {
            throw new ArgumentOutOfRangeException(nameof(window), "The window must be longer than the overlap, and both positive.");
        }

        if (durationSeconds <= 0)
        {
            return [];
        }

        var step = window - overlap;
        var count = durationSeconds <= window ? 1 : (int)Math.Ceiling((durationSeconds - overlap) / step);
        var windows = new List<AudioWindow>(count);
        for (var i = 0; i < count; i++)
        {
            var start = i * step;
            var end = Math.Min(start + window, durationSeconds);
            var keepFrom = i == 0 ? double.NegativeInfinity : start + (overlap / 2);
            var keepTo = i == count - 1 ? double.PositiveInfinity : start + step + (overlap / 2);
            windows.Add(new AudioWindow(i, start, end, keepFrom, keepTo));
        }

        return windows;
    }
}
