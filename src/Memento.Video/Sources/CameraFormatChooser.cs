namespace Memento.Video.Sources;

/// <summary>
/// Picks a camera's capture format: the largest picture up to the limit (1080p by default) that delivers at least
/// <c>minFrameRate</c>, preferring uncompressed NV12/YUY2 over MJPG at equal size (MJPG costs a decode). Pure, for tests.
/// </summary>
public static class CameraFormatChooser
{
    /// <summary>One native format: its index in the reader's list, size, frame rate and whether it is compressed.</summary>
    public sealed record Format(int Index, int Width, int Height, double FrameRate, bool Compressed);

    public static Format? Choose(IReadOnlyList<Format> formats, int maxWidth = 1920, int maxHeight = 1080, double minFrameRate = 24)
    {
        ArgumentNullException.ThrowIfNull(formats);
        var fitting = formats.Where(f => f.Width <= maxWidth && f.Height <= maxHeight && f.Width > 0).ToList();
        if (fitting.Count == 0)
        {
            return formats.OrderBy(f => (long)f.Width * f.Height).FirstOrDefault();
        }

        var fast = fitting.Where(f => f.FrameRate >= minFrameRate - 0.5).ToList();
        var pool = fast.Count > 0 ? fast : fitting;
        return pool
            .OrderByDescending(f => (long)f.Width * f.Height)
            .ThenBy(f => f.Compressed)
            .ThenBy(f => Math.Abs(f.FrameRate - 30))
            .First();
    }
}
