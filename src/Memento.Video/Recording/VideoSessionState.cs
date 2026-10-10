namespace Memento.Video.Recording;

public enum VideoSessionState
{
    Created,

    Recording,

    /// <summary>The whole recording is paused; the video timeline stops with the audio.</summary>
    Paused,

    /// <summary>The source has no pictures for now (window minimised, display disconnected); the last picture is held.</summary>
    Waiting,

    /// <summary>The track ended before the recording (window closed, disk full, encoder gone); see <see cref="VideoSessionStatus.Error"/>.</summary>
    Ended,

    Stopped,
}
