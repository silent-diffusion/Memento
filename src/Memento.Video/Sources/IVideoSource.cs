namespace Memento.Video.Sources;

/// <summary>
/// A started video source: a display, a window or a camera. It keeps only its latest picture (on the GPU) and never
/// waits for whoever reads it, so a slow encoder can only make frames be skipped, never stall capture. Pictures arrive
/// on the source's own threads; <see cref="TryGetLatest"/> is called by the session's pacing thread.
/// </summary>
public interface IVideoSource : IAsyncDisposable
{
    VideoSourceId Id { get; }

    /// <summary>What the user sees: "Display 1", "Zoom Meeting", the camera's name.</summary>
    string Name { get; }

    VideoSourceState State { get; }

    /// <summary>Pictures delivered by Windows so far, and when.</summary>
    VideoSourceStatistics Statistics { get; }

    /// <summary>Set when the source went away or failed; <see cref="Lost"/> was raised with it.</summary>
    VideoCaptureException? Loss { get; }

    /// <summary>Raised once, on a thread-pool thread, when the source goes away (display unplugged, window closed, camera removed).</summary>
    event EventHandler<VideoSourceLostEventArgs>? Lost;

    /// <summary>Raised when the source pauses (window minimised, camera taken) or resumes.</summary>
    event EventHandler<VideoSourceStateChangedEventArgs>? StateChanged;

    /// <summary>Starts delivering pictures. Throws <see cref="VideoCaptureException"/> with a specific code when it cannot.</summary>
    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The latest picture, or false before the first one. The frame's texture stays valid until the next call; the
    /// caller must finish submitting its GPU work on it before calling again.
    /// </summary>
    bool TryGetLatest(out VideoFrame frame);

    Task StopAsync();
}
