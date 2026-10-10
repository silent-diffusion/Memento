using Memento.Video.Encoding;

namespace Memento.Video.Recording;

/// <summary>How one video track is recorded.</summary>
/// <param name="TrackId">Its id in the manifest (<c>screen</c>, <c>camera</c>).</param>
/// <param name="Role"><c>screen</c> or <c>camera</c>.</param>
/// <param name="ProjectFolder">The project folder; files go to <c>tracks/&lt;id&gt;.mp4</c> (then <c>.part2.mp4</c> ...).</param>
/// <param name="TimelineOriginQpc">The QPC time of recording position 0 with the pauses so far left out (the audio session's start).</param>
public sealed record VideoSessionOptions(
    string TrackId,
    string Role,
    string ProjectFolder,
    VideoEncoderSettings Settings,
    long TimelineOriginQpc)
{
    /// <summary>Relative path of part <paramref name="part"/> (1-based), the audio rollover's naming.</summary>
    public string RelativePartPath(int part) =>
        part <= 1 ? $"tracks/{TrackId}.mp4" : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"tracks/{TrackId}.part{part}.mp4");
}
