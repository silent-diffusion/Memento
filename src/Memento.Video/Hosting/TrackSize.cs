using Memento.Core.Settings;
using Memento.Video.Encoding;

namespace Memento.Video.Hosting;

/// <summary>The encoded size of a track from what is captured and Settings › Recording › Video defaults › Resolution.</summary>
public static class TrackSize
{
    /// <summary>A display or window of <paramref name="width"/> × <paramref name="height"/>, kept as it is ("Match display") or fitted inside 1080p or 720p.</summary>
    public static (int Width, int Height) ForScreen(int width, int height, string resolution)
    {
        if (width <= 0 || height <= 0)
        {
            (width, height) = (1920, 1080);
        }

        var (maxWidth, maxHeight) = resolution switch
        {
            VideoSettings.Resolution1080 => (1920, 1080),
            VideoSettings.Resolution720 => (1280, 720),
            _ => (int.MaxValue, int.MaxValue),
        };

        // A portrait display fits the box turned on its side.
        if (height > width && maxWidth != int.MaxValue)
        {
            (maxWidth, maxHeight) = (maxHeight, maxWidth);
        }

        var scale = Math.Min(1.0, Math.Min((double)maxWidth / width, (double)maxHeight / height));
        return VideoEncoderSettings.EncodableSize((int)(width * scale), (int)(height * scale));
    }

    /// <summary>A camera: 1080p unless Settings asks for 720p (its picture is letterboxed into the track).</summary>
    public static (int Width, int Height) ForCamera(string resolution) =>
        resolution == VideoSettings.Resolution720 ? (1280, 720) : (1920, 1080);

    public static VideoQuality Quality(string quality) => quality switch
    {
        VideoSettings.QualitySmaller => VideoQuality.Smaller,
        VideoSettings.QualityBest => VideoQuality.Best,
        _ => VideoQuality.Balanced,
    };
}
