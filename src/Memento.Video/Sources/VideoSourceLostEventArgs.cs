namespace Memento.Video.Sources;

/// <summary>A source went away at <see cref="AtQpc"/>; <see cref="Error"/> says what happened in the §18.6 wording.</summary>
public sealed class VideoSourceLostEventArgs(VideoSourceId source, VideoCaptureException error, long atQpc) : EventArgs
{
    public VideoSourceId Source { get; } = source;

    public VideoCaptureException Error { get; } = error;

    public long AtQpc { get; } = atQpc;
}
