namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>project.get</c>: everything Review needs for one recording.</summary>
/// <param name="MixUrl"><c>https://library.memento/&lt;id&gt;/mix.flac</c> once finalized.</param>
/// <param name="PeaksUrl"><c>https://library.memento/&lt;id&gt;/peaks.json</c> once finalized.</param>
/// <param name="SizeBytes">Everything in the project folder.</param>
/// <param name="MixOnly">2.0: set once "Keep only the mix" removed the separate track files; <c>null</c> while they are kept.</param>
/// <param name="Video">2.0: the recording's video at a glance; <c>null</c> for an audio recording.</param>
/// <param name="VideoTracks">2.0: each video track with its URL and timeline offset (<c>[]</c> for an audio recording).</param>
/// <param name="Pip">2.0: the camera inset as it was last set while recording; <c>null</c> without a camera track.</param>
public sealed record Project(
    RecordingSummary Summary,
    RecordingDetails Details,
    IReadOnlyList<Track> Tracks,
    string? MixUrl,
    string? PeaksUrl,
    IReadOnlyList<Chapter> Chapters,
    IReadOnlyList<Highlight> Highlights,
    IReadOnlyList<Topic> Topics,
    IReadOnlyList<HistoryEntry> History,
    IntegrityInfo Integrity,
    long SizeBytes,
    MixOnlyInfo? MixOnly = null,
    VideoSummary? Video = null,
    IReadOnlyList<VideoTrackInfo>? VideoTracks = null,
    PipState? Pip = null)
{
    /// <summary>Never null on the wire: <c>[]</c> for an audio recording.</summary>
    public IReadOnlyList<VideoTrackInfo> VideoTracks { get; init; } = VideoTracks ?? [];
}
