namespace Memento.Video.Encoding;

/// <summary>
/// One video track's encoding. The bitrate follows the picture size and frame rate from <see cref="Quality"/>
/// (<see cref="VideoBitrate"/>): Balanced is about 12 Mb/s at 1440p 30 fps, as Settings says.
/// </summary>
/// <param name="Width">Encoded width (even).</param>
/// <param name="Height">Encoded height (even).</param>
/// <param name="FrameRate">Constant output frame rate: 24, 30 or 60.</param>
/// <param name="PreferHardware">Settings' Hardware encoder switch: try the GPU first, fall back to software.</param>
/// <param name="KeyframeIntervalSeconds">Each key frame starts a new MP4 fragment, so this is also the most a crash can lose.</param>
public sealed record VideoEncoderSettings(
    int Width,
    int Height,
    int FrameRate = 30,
    VideoQuality Quality = VideoQuality.Balanced,
    bool PreferHardware = true,
    int KeyframeIntervalSeconds = 2)
{
    public int BitrateBitsPerSecond => VideoBitrate.For(Width, Height, FrameRate, Quality);

    /// <summary>Duration of one frame in 100 ns ticks.</summary>
    public long FrameDurationTicks => 10_000_000L / FrameRate;

    /// <summary>Rounds an arbitrary capture size down to what H.264 encoders accept (even, at least 64 × 64, at most 4096 wide).</summary>
    public static (int Width, int Height) EncodableSize(int width, int height, int maxWidth = 4096)
    {
        if (width > maxWidth)
        {
            height = (int)((long)height * maxWidth / width);
            width = maxWidth;
        }

        return (Math.Max(64, width & ~1), Math.Max(64, height & ~1));
    }
}
