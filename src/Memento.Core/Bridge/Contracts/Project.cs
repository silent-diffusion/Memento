namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>project.get</c>: everything Review needs for one recording.</summary>
/// <param name="MixUrl"><c>https://library.memento/&lt;id&gt;/mix.flac</c> once finalized.</param>
/// <param name="PeaksUrl"><c>https://library.memento/&lt;id&gt;/peaks.json</c> once finalized.</param>
/// <param name="SizeBytes">Everything in the project folder.</param>
/// <param name="MixOnly">2.0: set once "Keep only the mix" removed the separate track files; <c>null</c> while they are kept.</param>
/// <param name="Video">2.0: the video tracks Review plays; <c>null</c> when the recording never had video.</param>
/// <param name="AudioRemoved">2.1: set once "Delete audio" removed the tracks, the mix and the peaks (then <c>mixUrl</c> and <c>peaksUrl</c> are null); <c>null</c> while they are kept.</param>
/// <param name="MixLevels">After 2.0.0-beta.1: the gains "Even out source levels" gave each track in the mix; <c>null</c> when the mix sums the tracks as recorded.</param>
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
    ProjectVideo? Video = null,
    MixLevelsInfo? MixLevels = null,
    AudioRemovedInfo? AudioRemoved = null);
