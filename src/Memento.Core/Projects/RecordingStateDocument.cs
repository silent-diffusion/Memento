using System.Text.Json;
using System.Text.Json.Serialization;

namespace Memento.Core.Projects;

/// <summary>
/// <c>recording.state.json</c>: exists only while a recording is in progress (or until it is finalized) and
/// drives crash recovery. Rewritten atomically at the start, at every checkpoint and at every state change.
/// </summary>
public sealed record RecordingStateDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public required string SessionId { get; init; }

    public required string RecordingId { get; init; }

    public DateTimeOffset StartedAt { get; init; }

    /// <summary><c>recording</c>, <c>paused</c> or <c>stopped</c> (writers closed, finalize pending).</summary>
    public string State { get; init; } = "recording";

    public DateTimeOffset? LastCheckpointAt { get; init; }

    /// <summary>Recorded time at the last checkpoint.</summary>
    public long ElapsedMsAtCheckpoint { get; init; }

    public int CheckpointSeconds { get; init; }

    public IReadOnlyList<RecordingStateTrack> Tracks { get; init; } = [];

    public IReadOnlyList<ProjectPause> Pauses { get; init; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
