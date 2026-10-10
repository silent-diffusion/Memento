namespace Memento.Video.Sources;

public enum VideoSourceState
{
    Created,

    Running,

    /// <summary>No pictures for now: the captured window is minimised, or the camera is held by another app. The track continues with the last picture.</summary>
    Paused,

    /// <summary>Gone for good (display unplugged, window closed, camera removed); see <see cref="IVideoSource.Loss"/>.</summary>
    Lost,

    Stopped,
}
