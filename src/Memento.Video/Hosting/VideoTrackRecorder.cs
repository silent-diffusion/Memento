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

    public VideoTrackRecorder(VideoCaptureSession session, VideoDevice device, string role, string sourceId, Func<VideoSourceId, IVideoSource> openSource)
    {
        _session = session;
        _device = device;
        _openSource = openSource;
        Role = role;
        SourceId = sourceId;
        _session.Problem += OnProblem;
    }

    public event EventHandler<VideoProblemEventArgs>? Problem;

    public string TrackId => _session.TrackId;

    public string Role { get; }

    public string SourceId { get; private set; }

    public void PauseTrack() => _session.Pause();

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
            SourceId = sourceId;
        }
        catch (VideoCaptureException ex)
        {
            if (source is not null)
            {
                await source.DisposeAsync().ConfigureAwait(false);
            }

            throw new VideoStartException(VideoCodes.Of(ex.Code), ex.Message + " The track keeps recording its previous source.", ex);
        }
    }

    public VideoTrackCheckpoint Checkpoint(bool flush) => _session.Checkpoint(flush);

    public VideoTrackStatus Status()
    {
        var s = _session.Status();
        return new VideoTrackStatus(
            s.TrackId,
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
            s.DurationMs,
            s.StartOffsetMs);
    }

    public Task<VideoTrack> StopAsync(CancellationToken cancellationToken) => _session.StopAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        _session.Problem -= OnProblem;
        await _session.DisposeAsync().ConfigureAwait(false);
        _device.Dispose();
    }

    private void OnProblem(object? sender, VideoSessionEventArgs e) =>
        Problem?.Invoke(this, new VideoProblemEventArgs(e.TrackId, Role, VideoCodes.Of(e.State), VideoCodes.Of(e.Error.Code), e.Error.Message, e.AtMs));
}
