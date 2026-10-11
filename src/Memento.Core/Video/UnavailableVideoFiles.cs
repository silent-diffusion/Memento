namespace Memento.Core.Video;

/// <summary>No Media Foundation video support in this build or host: files are reported unreadable and nothing is written.</summary>
public sealed class UnavailableVideoFiles : IVideoFiles
{
    public bool IsAvailable => false;

    public VideoFileRepair Repair(string path) => new(File.Exists(path), 0, 0, 0);

    public Task<VideoJoinResult> JoinAsync(IReadOnlyList<VideoJoinPart> parts, string outputPath, bool reencode, IProgress<double>? progress, CancellationToken cancellationToken) =>
        Task.FromException<VideoJoinResult>(new VideoStartException(Bridge.DomainErrorCodes.VideoEncoderUnavailable, "Video files can't be joined in this copy of Memento. The recorded parts are kept unchanged."));

    public Task ComposeAsync(VideoComposeRequest request, IProgress<double>? progress, CancellationToken cancellationToken) =>
        Task.FromException(new VideoStartException(Bridge.DomainErrorCodes.VideoEncoderUnavailable, "Video can't be exported in this copy of Memento. Nothing inside Memento was changed."));
}
