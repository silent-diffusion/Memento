using Memento.Video.Gpu;

namespace Memento.Video.Encoding;

/// <summary>Opens <see cref="Mp4TrackWriter"/>s on one device.</summary>
public sealed class Mp4TrackWriterFactory(VideoDevice device) : IVideoTrackWriterFactory
{
    public IVideoTrackWriter Create(string path, VideoEncoderSettings settings) => Mp4TrackWriter.Create(device, path, settings);
}
