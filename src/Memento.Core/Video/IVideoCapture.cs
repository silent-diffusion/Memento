using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Video;

/// <summary>
/// Display, window and camera capture (ARCHITECTURE.md §5.x), implemented by Memento.Video. Core only orchestrates it:
/// the recording coordinator starts one <see cref="IVideoTrackRecorder"/> per role, beside the audio session, and never
/// lets a video failure reach the audio capture. <see cref="UnavailableVideoCapture"/> stands in where there is none.
/// </summary>
public interface IVideoCapture
{
    /// <summary>Whether this PC and build can record video at all (<c>sources.list</c>'s <c>videoAvailable</c>).</summary>
    bool IsAvailable { get; }

    /// <summary>Lists displays, windows and cameras without opening any of them.</summary>
    VideoSourcesResult ListSources();

    /// <summary>One small still of a display or window (or a camera) for the picker; nothing is written to disk.</summary>
    /// <exception cref="VideoStartException">The source is not there, is protected, or capture is blocked.</exception>
    Task<VideoThumbnailResult> ThumbnailAsync(string sourceId, int width, CancellationToken cancellationToken);

    /// <summary>Starts one video track into <c>tracks/&lt;track id&gt;.mp4</c> of the project folder.</summary>
    /// <exception cref="VideoStartException">The source or the encoder could not start; nothing was written.</exception>
    Task<IVideoTrackRecorder> StartTrackAsync(VideoTrackRequest request, CancellationToken cancellationToken);
}
