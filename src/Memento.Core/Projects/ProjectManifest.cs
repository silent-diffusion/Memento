using System.Text.Json;
using System.Text.Json.Serialization;
using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Projects;

/// <summary>
/// <c>project.json</c>, schema v1 (ARCHITECTURE.md §4). Fields this build does not know are kept in
/// <see cref="ExtensionData"/> and written back unchanged.
/// </summary>
public sealed record ProjectManifest
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public required string Id { get; init; }

    /// <summary>Start of the recording in local time with its UTC offset.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Bumped by every write.</summary>
    public DateTimeOffset ModifiedAt { get; init; }

    /// <summary>One of <see cref="ProjectStates"/>.</summary>
    public string State { get; init; } = ProjectStates.Recording;

    public ProjectDetails Details { get; init; } = new();

    /// <summary>Recorded time, excluding pauses.</summary>
    public long DurationMs { get; init; }

    /// <summary>Reserved for video capture (not in 1.0).</summary>
    public bool HasVideo { get; init; }

    public IReadOnlyList<ProjectTrack> Tracks { get; init; } = [];

    /// <summary>Reserved for video tracks (not in 1.0); kept so adding video does not change the schema.</summary>
    public IReadOnlyList<JsonElement> VideoTracks { get; init; } = [];

    /// <summary>Pauses, so transcript timings stay continuous across the gap.</summary>
    public IReadOnlyList<ProjectPause> Pauses { get; init; } = [];

    /// <summary>The playback mix, once finalized.</summary>
    public ProjectMix? Mix { get; init; }

    /// <summary>Relative path of the waveform peaks file, once finalized.</summary>
    public string? Peaks { get; init; }

    /// <summary>Processing stages in pipeline order.</summary>
    public IReadOnlyList<StageStatus> Stages { get; init; } = [];

    public ProjectIntegrity Integrity { get; init; } = new();

    /// <summary>Set when the project was repaired at launch after an interrupted recording.</summary>
    public ProjectRecovery? Recovery { get; init; }

    /// <summary>The last failure of each failed stage (M2), with what was kept and the remedies offered.</summary>
    public IReadOnlyList<ProjectStageFailure> Failures { get; init; } = [];

    /// <summary>How the next transcription pass runs when it differs from Settings (M2); <c>null</c> otherwise.</summary>
    public ProcessingRequest? Processing { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
