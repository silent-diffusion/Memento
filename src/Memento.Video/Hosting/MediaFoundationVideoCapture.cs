using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Video;
using Memento.Video.Encoding;
using Memento.Video.Gpu;
using Memento.Video.Recording;
using Memento.Video.Sources;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Memento.Video.Hosting;

/// <summary>
/// The app's <see cref="IVideoCapture"/> (ARCHITECTURE.md §5.x): lists displays, windows and cameras, makes picker
/// thumbnails, and starts one <see cref="VideoCaptureSession"/> per track on a Direct3D device of its own (on the
/// adapter that drives a captured display). Nothing here is called from an audio thread.
/// </summary>
public sealed class MediaFoundationVideoCapture(ILoggerFactory? loggers = null) : IVideoCapture
{
    private readonly Lazy<bool> _supported = new(GraphicsCaptureSource.IsSupported);
    private readonly ILoggerFactory _loggers = loggers ?? NullLoggerFactory.Instance;

    public bool IsAvailable => _supported.Value;

    public VideoSourcesResult ListSources()
    {
        if (!IsAvailable)
        {
            return new VideoSourcesResult([], [], []);
        }

        return new VideoSourcesResult(
            VideoSourceEnumerator.ListDisplays().Select(ToContract).ToList(),
            VideoSourceEnumerator.ListWindows(Environment.ProcessId).Select(ToContract).ToList(),
            VideoSourceEnumerator.ListCameras().Select(ToContract).ToList());
    }

    public Task<VideoThumbnailResult> ThumbnailAsync(string sourceId, int width, CancellationToken cancellationToken)
    {
        var (id, info) = Find(sourceId);
        return Task.Run(
            () =>
            {
                try
                {
                    return Thumbnails.Take(id, info, width);
                }
                catch (VideoCaptureException ex)
                {
                    throw new VideoStartException(VideoCodes.Of(ex.Code), ex.Message, ex);
                }
            },
            cancellationToken);
    }

    public async Task<IVideoTrackRecorder> StartTrackAsync(VideoTrackRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsAvailable)
        {
            throw new VideoStartException(DomainErrorCodes.VideoUnavailable, VideoErrors.CaptureUnsupported());
        }

        var (id, info) = Find(request.SourceId);
        var settings = request.Settings;
        var (width, height) = id.Kind == VideoSourceKind.Camera
            ? TrackSize.ForCamera(settings.Resolution)
            : TrackSize.ForScreen(info.Width, info.Height, settings.Resolution);
        var encoder = new VideoEncoderSettings(width, height, settings.FrameRate, TrackSize.Quality(settings.Quality), settings.HardwareEncoder);
        var capture = new GraphicsCaptureOptions(IncludeCursor: settings.IncludePointer);

        VideoDevice? device = null;
        VideoCaptureSession? session = null;
        try
        {
            device = VideoDevice.Create(id.Kind == VideoSourceKind.Display ? VideoDevice.AdapterForDisplay(id.Key) : null);
            var onDevice = device;
            IVideoSource Open(VideoSourceId sourceId) => VideoSourceFactory.Create(onDevice, sourceId, NameOf(sourceId), capture);
            session = new VideoCaptureSession(
                new VideoSessionOptions(request.TrackId, request.Role, request.ProjectFolder, encoder, request.TimelineOriginQpc),
                Open(id),
                new Mp4TrackWriterFactory(device),
                _loggers.CreateLogger<VideoCaptureSession>());
            await session.StartAsync(cancellationToken).ConfigureAwait(false);
            var recorder = new VideoTrackRecorder(session, device, request.Role, id.ToString(), Open);
            session = null;
            device = null;
            return recorder;
        }
        catch (VideoCaptureException ex)
        {
            throw new VideoStartException(VideoCodes.Of(ex.Code), ex.Message, ex);
        }
        finally
        {
            if (session is not null)
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }

            device?.Dispose();
        }
    }

    private static VideoSource ToContract(VideoSourceInfo info) =>
        new(
            info.Id.ToString(),
            VideoCaptureSession.SourceKindName(info.Id.Kind),
            info.Name,
            info.Detail,
            info.Width,
            info.Height,
            info.RefreshRate,
            info.IsPrimary,
            info.IsExcluded,
            info.IsProtected,
            info.ProcessName);

    /// <summary>The listed source for an id, or <c>video.sourceNotFound</c> / <c>video.protectedContent</c>.</summary>
    private static (VideoSourceId Id, VideoSourceInfo Info) Find(string sourceId)
    {
        if (!VideoSourceId.TryParse(sourceId, out var id))
        {
            throw new VideoStartException(DomainErrorCodes.VideoSourceNotFound, "That video source is not one Memento listed. Audio recording is not affected; choose a display, window or camera from the list.");
        }

        IReadOnlyList<VideoSourceInfo> listed = id.Kind switch
        {
            VideoSourceKind.Display => VideoSourceEnumerator.ListDisplays(),
            VideoSourceKind.Window => VideoSourceEnumerator.ListWindows(Environment.ProcessId),
            _ => VideoSourceEnumerator.ListCameras(),
        };
        var info = listed.FirstOrDefault(s => s.Id == id);
        if (info is null)
        {
            throw new VideoStartException(
                DomainErrorCodes.VideoSourceNotFound,
                id.Kind switch
                {
                    VideoSourceKind.Display => VideoErrors.DisplayNotFound("That display"),
                    VideoSourceKind.Window => VideoErrors.WindowNotFound("That window"),
                    _ => VideoErrors.CameraNotFound("That camera"),
                });
        }

        if (info.IsProtected)
        {
            throw new VideoStartException(DomainErrorCodes.VideoProtectedContent, VideoErrors.ProtectedWindow(info.Name));
        }

        return (id, info);
    }

    private static string NameOf(VideoSourceId id)
    {
        try
        {
            return Find(id.ToString()).Info.Name;
        }
        catch (VideoStartException)
        {
            return id.Kind == VideoSourceKind.Camera ? "The camera" : id.Kind == VideoSourceKind.Window ? "The window" : "The display";
        }
    }
}
