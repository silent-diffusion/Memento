namespace Memento.Video.Encoding;

/// <summary>Opens track writers (a seam so the session can be tested without a graphics card).</summary>
public interface IVideoTrackWriterFactory
{
    /// <summary>Opens <paramref name="path"/>; throws <see cref="VideoCaptureException"/> (<see cref="VideoErrorCode.EncoderUnavailable"/>) when no encoder opens.</summary>
    IVideoTrackWriter Create(string path, VideoEncoderSettings settings);
}
