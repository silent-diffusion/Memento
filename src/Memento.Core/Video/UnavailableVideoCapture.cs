using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Video;

/// <summary>No video capture in this build or host (tests, tools): nothing is listed and every start is refused.</summary>
public sealed class UnavailableVideoCapture : IVideoCapture
{
    public const string Message = "Video can't be recorded in this copy of Memento. Audio recording is not affected.";

    public bool IsAvailable => false;

    public VideoSourcesResult ListSources() => new([], [], []);

    public Task<VideoThumbnailResult> ThumbnailAsync(string sourceId, int width, CancellationToken cancellationToken) =>
        Task.FromException<VideoThumbnailResult>(new VideoStartException(DomainErrorCodes.VideoSourceNotFound, Message));

    public Task<IVideoTrackRecorder> StartTrackAsync(VideoTrackRequest request, CancellationToken cancellationToken) =>
        Task.FromException<IVideoTrackRecorder>(new VideoStartException(DomainErrorCodes.VideoSourceNotFound, Message));
}
