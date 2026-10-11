namespace Memento.Core.Video;

/// <summary>
/// Work on recorded video files (Memento.Video, Media Foundation): repairing a torn file after a crash, joining a
/// track's parts, and composing export files. <see cref="UnavailableVideoFiles"/> stands in where there is none.
/// </summary>
public interface IVideoFiles
{
    bool IsAvailable { get; }

    /// <summary>
    /// Cuts an incomplete trailing fragment off a fragmented capture file in place (nothing else is touched) and says
    /// what it holds. A file that is whole or not fragmented is left as it is.
    /// </summary>
    VideoFileRepair Repair(string path);

    /// <summary>
    /// Joins a track's parts on the recording timeline into one ordinary MP4 at <paramref name="outputPath"/>. Parts
    /// from the same encoder are copied without decoding; parts from different encoders, or every part when
    /// <paramref name="reencode"/> (the <c>software</c> remedy), are encoded again in software.
    /// </summary>
    Task<VideoJoinResult> JoinAsync(IReadOnlyList<VideoJoinPart> parts, string outputPath, bool reencode, IProgress<double>? progress, CancellationToken cancellationToken);

    /// <summary>Writes one export file (DESIGN.md §18.4): a video track with the mixed audio, the camera inset burnt in or not.</summary>
    Task ComposeAsync(VideoComposeRequest request, IProgress<double>? progress, CancellationToken cancellationToken);
}
