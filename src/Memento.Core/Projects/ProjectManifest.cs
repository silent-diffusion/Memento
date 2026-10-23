using System.Text.Json;
using System.Text.Json.Serialization;
using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Projects;

/// <summary>
/// <c>project.json</c>, schema v5 (ARCHITECTURE.md §4). Fields this build does not know are kept in
/// <see cref="ExtensionData"/> and written back unchanged. v2 (0.4.0) makes <see cref="Attachments"/> a typed field;
/// v1 files carried the same array untyped, and <see cref="ProjectManifestMigrator"/> drops entries it cannot read. v3 (after
/// 1.2.0) adds <see cref="ProjectDetails.WhoSpoke"/> (<see cref="WhoSpokeMigration"/>). v4 (2.0) types
/// <see cref="VideoTracks"/> (<see cref="VideoTracksMigration"/>) and adds <see cref="VideoRemoved"/>. v5 (2.1) adds the
/// Library <see cref="Folder"/> (<see cref="FolderMigration"/>) and <see cref="AudioRemoved"/>. v6 (2.1) adds
/// <see cref="Segments"/> for "Resume recording" (<see cref="SegmentsMigration"/>).
/// </summary>
public sealed record ProjectManifest
{
    public const int CurrentSchemaVersion = 6;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public required string Id { get; init; }

    /// <summary>Start of the recording in local time with its UTC offset.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Bumped by every write.</summary>
    public DateTimeOffset ModifiedAt { get; init; }

    /// <summary>One of <see cref="ProjectStates"/>.</summary>
    public string State { get; set; } = ProjectStates.Recording;

    public ProjectDetails Details { get; set; } = new();

    /// <summary>Recorded time, excluding pauses.</summary>
    public long DurationMs { get; init; }

    /// <summary>The recording has (or had, before "Remove video") at least one video track (2.0).</summary>
    public bool HasVideo { get; init; }

    public IReadOnlyList<ProjectTrack> Tracks { get; set; } = [];

    /// <summary>
    /// Video tracks (schema v4, ARCHITECTURE.md §5.x), apart from the audio <see cref="Tracks"/> on purpose: every 1.x
    /// reader mixes, transcribes and hashes each entry of <c>tracks[]</c> as audio, and kept this list untouched. A setter
    /// rather than init, so a manifest without the field reads as an empty list.
    /// </summary>
    public IReadOnlyList<VideoTrack> VideoTracks { get; set; } = [];

    /// <summary>"Remove video, keep audio" deleted the video files (2.0); <c>null</c> while they are kept.</summary>
    public ProjectVideoRemoved? VideoRemoved { get; init; }

    /// <summary>
    /// 2.1: the Library folder the recording is filed in (<c>Clients/Acme</c>; <see cref="Library.FolderPaths"/>), or
    /// <c>null</c> for the root. Data only: changing it never moves a file.
    /// </summary>
    public string? Folder { get; init; }

    /// <summary>"Delete audio" removed the tracks, the mix and the peaks (2.1); <c>null</c> while they are kept.</summary>
    public ProjectAudioRemoved? AudioRemoved { get; init; }

    /// <summary>Where the camera inset was over the recording (<c>recording.setPip</c>), in time order; empty without a camera.</summary>
    public IReadOnlyList<ProjectPipChange> VideoPip { get; set; } = [];

    /// <summary>
    /// The recorded stretches (schema v5, 2.1): one for a recording made in one go, one more for each "Resume recording".
    /// A setter rather than init, so a manifest without the field reads as an empty list.
    /// </summary>
    public IReadOnlyList<ProjectSegment> Segments { get; set; } = [];

    /// <summary>Pauses, so transcript timings stay continuous across the gap.</summary>
    public IReadOnlyList<ProjectPause> Pauses { get; set; } = [];

    /// <summary>The playback mix, once finalized.</summary>
    public ProjectMix? Mix { get; init; }

    /// <summary>Relative path of the waveform peaks file, once finalized.</summary>
    public string? Peaks { get; init; }

    /// <summary>Processing stages in pipeline order.</summary>
    public IReadOnlyList<StageStatus> Stages { get; set; } = [];

    public ProjectIntegrity Integrity { get; set; } = new();

    /// <summary>Set when the project was repaired at launch after an interrupted recording.</summary>
    public ProjectRecovery? Recovery { get; init; }

    /// <summary>The last failure of each failed stage (M2), with what was kept and the remedies offered.</summary>
    public IReadOnlyList<ProjectStageFailure> Failures { get; set; } = [];

    /// <summary>How the next transcription pass runs when it differs from Settings (M2); <c>null</c> otherwise.</summary>
    public ProcessingRequest? Processing { get; init; }

    /// <summary>Files kept with the recording (M3): the original agenda and other attachments, in the order added.</summary>
    public IReadOnlyList<AttachmentRecord> Attachments { get; set; } = [];

    /// <summary>The file a <c>library.importMedia</c> recording was made from (M3); <c>null</c> for recordings.</summary>
    public ProjectImportSource? ImportedFrom { get; init; }

    /// <summary>
    /// "Keep only the mix" removed the separate track files (2.0); <c>null</c> while they are kept. An optional field
    /// within schema 3: older builds keep it through <see cref="ExtensionData"/>.
    /// </summary>
    public ProjectMixOnly? MixOnly { get; init; }

    /// <summary>
    /// A "Rebuild the mix" the <c>mix</c> stage has not finished yet (after 2.0.0-beta.1); <c>null</c> otherwise. An
    /// optional field within schema 4.
    /// </summary>
    public ProjectMixRebuild? MixRebuild { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
