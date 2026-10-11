using Memento.Core.Bridge.Contracts;
using Memento.Core.Projects;
using Memento.Core.Video;
using Memento.Video.Gpu;
using Memento.Video.Recording;
using Memento.Video.Sources;

namespace Memento.Video.Hosting;

/// <summary>
/// One recording's video track for Core (<see cref="IVideoTrackRecorder"/>): a <see cref="VideoCaptureSession"/> and the
/// Direct3D device it records on, with its problems and status in the bridge's terms.
/// </summary>
internal sealed class VideoTrackRecorder : IVideoTrackRecorder
{
    private readonly VideoCaptureSession _session;
    private readonly VideoDevice _device;
    private readonly Func<VideoSourceId, IVideoSource> _openSource;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Func<Export.Nv12Frame?>>? _cameras;

    public VideoTrackRecorder(VideoCaptureSession session, VideoDevice device, string role, string sourceId, Func<VideoSourceId, IVideoSource> openSource, System.Collections.Concurrent.ConcurrentDictionary<string, Func<Export.Nv12Frame?>>? cameras = null)
    {
        _session = session;
        _cameras = cameras;
        _device = device;
        _openSource = openSource;
        Role = role;
        SourceId = sourceId;
        _session.Problem += OnProblem;
        Register(sourceId);
    }

    public event EventHandler<VideoProblemEventArgs>? Problem;

    public string TrackId => _session.TrackId;

    public string Role { get; }

    public string SourceId { get; private set; }

    public void PauseTrack() => _session.Pause();

    public Task SetQualityAsync(string quality, CancellationToken cancellationToken)
    {
        try
        {
            _session.ChangeQuality(TrackSize.Quality(quality));
            return Task.CompletedTask;
        }
        catch (VideoCaptureException ex)
        {
            return Task.FromException(new VideoStartException(VideoCodes.Of(ex.Code), ex.Message, ex));
        }
    }

    public void ResumeTrack() => _session.Resume();

    public async Task SwitchSourceAsync(string sourceId, CancellationToken cancellationToken)
    {
        if (!VideoSourceId.TryParse(sourceId, out var id))
        {
            throw new VideoStartException(Core.Bridge.DomainErrorCodes.VideoSourceNotFound, "That video source is not one Memento listed. The track keeps its source; choose another from the list.");
        }

        IVideoSource? source = null;
        try
        {
            source = _openSource(id);
            await _session.SwitchSourceAsync(source, cancellationToken).ConfigureAwait(false);
            _cameras?.TryRemove(SourceId, out _);
            SourceId = sourceId;
            Register(sourceId);
        }
        catch (VideoCaptureException ex)
        {
            if (source is not null)
            {
                await source.DisposeAsync().ConfigureAwait(false);
            }

            throw new VideoStartException(VideoCodes.Of(ex.Code), ex.Message + " The track keeps recording its previous source.", ex, ex.Detail);
        }
    }

    public VideoTrackCheckpoint Checkpoint(bool flush) => _session.Checkpoint(flush);

    public VideoTrackStatus Status()
    {
        var s = _session.Status();
        return new VideoTrackStatus(
            Role,
            VideoCodes.Of(s.State),
            string.IsNullOrEmpty(s.SourceId) ? SourceId : s.SourceId,
            s.SourceName,
            s.Reason,
            s.EncoderName,
            s.HardwareEncoder,
            s.Width,
            s.Height,
            s.FrameRate,
            s.TargetBitsPerSecond,
            s.ActualBitsPerSecond,
            s.FramesWritten,
            s.FramesDropped,
            s.BytesWritten,
            s.DurationMs);
    }

    public Task<VideoTrack> StopAsync(CancellationToken cancellationToken) => _session.StopAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        _session.Problem -= OnProblem;
        _cameras?.TryRemove(SourceId, out _);
        await _session.DisposeAsync().ConfigureAwait(false);
        _device.Dispose();
    }

    private void Register(string sourceId)
    {
        if (_cameras is not null && sourceId.StartsWith("camera:", StringComparison.Ordinal))
        {
            _cameras[sourceId] = () => (_session.Source as ICameraSnapshot)?.Snapshot();
        }
    }

    private void OnProblem(object? sender, VideoSessionEventArgs e)
    {
        var kind = VideoSourceId.TryParse(SourceId, out var id) ? id.Kind : VideoSourceKind.Display;
        var code = VideoCodes.Problem(e.Error.Code, kind);
        var status = _session.Status();
        var detail = code == VideoProblemCodes.EncoderFallback ? status.EncoderName : e.Error.Detail;
        Problem?.Invoke(this, new VideoProblemEventArgs(e.TrackId, Role, VideoProblemCodes.StateOf(code), code, SourceId, status.SourceName, detail, e.Error.Message, e.AtMs));
    }
}
