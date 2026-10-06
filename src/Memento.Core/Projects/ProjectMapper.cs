using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Projects;

/// <summary>Turns stored project data into bridge contracts.</summary>
public static class ProjectMapper
{
    public static RecordingSummary ToSummary(ProjectManifest manifest, long sizeBytes)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return BuildSummary(
            manifest.Id,
            manifest.Details.Title,
            manifest.Details.Type,
            manifest.CreatedAt,
            manifest.DurationMs,
            manifest.HasVideo,
            manifest.Details.Participants,
            manifest.Stages,
            manifest.State,
            sizeBytes);
    }

    /// <summary>Builds a summary, applying the Library's stage visibility rule (see <see cref="VisibleStages"/>).</summary>
    public static RecordingSummary BuildSummary(
        string id,
        string title,
        string type,
        DateTimeOffset createdAt,
        long durationMs,
        bool hasVideo,
        IReadOnlyList<string> people,
        IReadOnlyList<StageStatus> stages,
        string state,
        long sizeBytes) =>
        new(
            id,
            title,
            type,
            createdAt,
            durationMs,
            people.Count,
            hasVideo,
            VisibleStages(stages),
            people,
            IsProcessing(stages),
            state,
            sizeBytes);

    /// <summary>
    /// Row and card pills: a finished <c>stored</c> stage is left out, so a recording with no other processing
    /// shows "Audio only" (DESIGN.md §4) instead of a "Stored" pill. Active or failed <c>stored</c> is shown.
    /// </summary>
    public static IReadOnlyList<StageStatus> VisibleStages(IReadOnlyList<StageStatus> stages)
    {
        ArgumentNullException.ThrowIfNull(stages);
        return stages.Where(s => !(s.Stage == StageNames.Stored && s.State == StageStates.Done)).ToList();
    }

    public static bool IsProcessing(IReadOnlyList<StageStatus> stages) => stages.Any(s => StageStates.IsRunning(s.State));

    public static Track ToTrack(ProjectTrack track)
    {
        ArgumentNullException.ThrowIfNull(track);
        return new Track(
            track.Id,
            track.SourceId,
            track.SourceKind,
            track.Name,
            track.File.Replace('\\', '/'),
            track.SampleRate,
            track.Channels,
            track.DurationMs,
            track.Sha256,
            track.StartOffsetMs,
            track.EndedEarlyAtMs);
    }

    public static RecordingDetails ToDetails(ProjectDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        return new RecordingDetails(
            details.Title,
            details.Type,
            details.Participants,
            details.Purpose,
            details.Platform,
            details.Organization,
            details.Location,
            details.Notes,
            details.Tags,
            details.Agenda);
    }
}
