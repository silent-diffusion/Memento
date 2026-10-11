using Memento.Core.Bridge.Contracts;
using Memento.Core.Projects;

namespace Memento.Core.Video;

/// <summary>
/// One running video track (Memento.Video's capture session). Its pacing and encoding run on its own threads; no call
/// here waits on capture or encoding for longer than one frame, and nothing it does touches the audio capture.
/// </summary>
public interface IVideoTrackRecorder : IAsyncDisposable
{
    string TrackId { get; }

    /// <summary><c>screen</c> or <c>camera</c>.</summary>
    string Role { get; }

    /// <summary>The source being captured now (it changes with <see cref="SwitchSourceAsync"/>).</summary>
    string SourceId { get; }

    /// <summary>Raised (on a thread-pool thread) when the user must be told something: a source lost, an encoder fallback, the track ending.</summary>
    event EventHandler<VideoProblemEventArgs>? Problem;

    /// <summary>The recording paused: the video timeline stops with the audio tracks.</summary>
    void PauseTrack();

    /// <summary>The recording resumed: the next frame follows the last one, leaving the paused time out.</summary>
    void ResumeTrack();

    /// <summary>Captures another display or window (or camera) into the same track; it keeps its size.</summary>
    /// <exception cref="VideoStartException">The new source could not start; the track keeps its old one.</exception>
    Task SwitchSourceAsync(string sourceId, CancellationToken cancellationToken);

    /// <summary>Continues at another quality (<c>smaller</c>, <c>balanced</c>, <c>best</c>): the §18.6 banner's Lower quality.</summary>
    Task SetQualityAsync(string quality, CancellationToken cancellationToken);

    /// <summary>Flushes the file to disk (when <paramref name="flush"/>) and returns the entry for <c>recording.state.json</c>.</summary>
    VideoTrackCheckpoint Checkpoint(bool flush);

    /// <summary>What the Encoder card shows now.</summary>
    VideoTrackStatus Status();

    /// <summary>Stops the track, closes its file (a fragmented MP4, playable as written) and returns the manifest entry.</summary>
    Task<VideoTrack> StopAsync(CancellationToken cancellationToken);
}
