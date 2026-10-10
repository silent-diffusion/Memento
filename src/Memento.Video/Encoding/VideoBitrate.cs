namespace Memento.Video.Encoding;

/// <summary>
/// Bitrates per quality, scaled by pixels per second from three anchors at 1440p 30 fps: Smaller 6, Balanced 12, Best
/// 20 Mb/s (DESIGN.md §18.5: "Balanced is about 12 Mb/s at 1440p"). Screen content is mostly still, so a constant
/// bitrate would waste space; the encoder runs peak-constrained VBR with these as the mean (ENGINE-NOTES.md §M.3).
/// </summary>
public static class VideoBitrate
{
    private const double ReferencePixelsPerSecond = 2560.0 * 1440 * 30;

    public static int For(int width, int height, int frameRate, VideoQuality quality)
    {
        var anchor = quality switch
        {
            VideoQuality.Smaller => 6_000_000,
            VideoQuality.Best => 20_000_000,
            _ => 12_000_000,
        };

        // Bitrate grows slower than the pixel rate (bigger pictures compress better): scale by the ratio to the power 0.75.
        var ratio = (double)width * height * frameRate / ReferencePixelsPerSecond;
        var scaled = anchor * Math.Pow(ratio, 0.75);
        var rounded = (int)Math.Round(scaled / 100_000) * 100_000;
        return Math.Clamp(rounded, 500_000, 80_000_000);
    }

    /// <summary>Bytes per hour at <paramref name="bitsPerSecond"/>, for the storage estimate ("about 9 hours at this quality").</summary>
    public static long BytesPerHour(int bitsPerSecond) => (long)bitsPerSecond / 8 * 3600;
}
