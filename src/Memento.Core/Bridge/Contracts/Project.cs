namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>project.get</c>: everything Review needs for one recording.</summary>
/// <param name="MixUrl"><c>https://library.memento/projects/&lt;id&gt;/mix.flac</c> once finalized.</param>
/// <param name="PeaksUrl"><c>https://library.memento/projects/&lt;id&gt;/peaks.json</c> once finalized.</param>
/// <param name="SizeBytes">Everything in the project folder.</param>
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
    long SizeBytes);
