namespace Memento.Video.Recording;

/// <summary>Something the user must be told about one video track: a source lost, an encoder fallback, the track ending.</summary>
/// <param name="AtMs">Recording position it happened at.</param>
public sealed class VideoSessionEventArgs(string trackId, VideoSessionState state, VideoCaptureException error, long atMs) : EventArgs
{
    public string TrackId { get; } = trackId;

    public VideoSessionState State { get; } = state;

    public VideoCaptureException Error { get; } = error;

    public long AtMs { get; } = atMs;
}
