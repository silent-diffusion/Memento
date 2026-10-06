using System.Text.Json;
using System.Text.Json.Serialization;

namespace Memento.Core.Settings;

/// <summary>Settings › Recording. Added in M1 with defaults, so M0 settings files read unchanged.</summary>
public sealed record RecordingSettings
{
    public const int DefaultCheckpointSeconds = 30;
    public const int MinCheckpointSeconds = 5;
    public const int MaxCheckpointSeconds = 300;
    public const int DefaultLowSpaceGb = 10;
    public const int MinLowSpaceGb = 1;
    public const int MaxLowSpaceGb = 500;
    public const string DefaultRecordingType = "meeting";

    public string DefaultType { get; init; } = DefaultRecordingType;

    /// <summary>The remembered source selection.</summary>
    public IReadOnlyList<string> DefaultSourceIds { get; init; } = [];

    /// <summary>Fixed to <c>true</c> in M1: every source is its own track.</summary>
    public bool KeepSeparateTracks { get; init; } = true;

    public StorageSettings Storage { get; init; } = new();

    public int CheckpointSeconds { get; init; } = DefaultCheckpointSeconds;

    public int LowSpaceGb { get; init; } = DefaultLowSpaceGb;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    /// <summary>The low-space threshold in bytes (GB here means GiB, as Windows Explorer shows it).</summary>
    [JsonIgnore]
    public long LowSpaceThresholdBytes => LowSpaceGb * 1024L * 1024 * 1024;

    /// <summary>Returns the first problem, worded for people, or <c>null</c> when every value is in range.</summary>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(DefaultType) || DefaultType.Length > 64)
        {
            return "The default recording type needs a name of 1 to 64 characters.";
        }

        if (!KeepSeparateTracks)
        {
            return "Separate tracks can't be turned off in this version; every source is saved as its own track.";
        }

        if (CheckpointSeconds is < MinCheckpointSeconds or > MaxCheckpointSeconds)
        {
            return $"Checkpoint every {CheckpointSeconds} s is out of range. Choose {MinCheckpointSeconds} to {MaxCheckpointSeconds} seconds.";
        }

        if (LowSpaceGb is < MinLowSpaceGb or > MaxLowSpaceGb)
        {
            return $"A low-space warning at {LowSpaceGb} GB is out of range. Choose {MinLowSpaceGb} to {MaxLowSpaceGb} GB.";
        }

        if (DefaultSourceIds.Count > 32 || DefaultSourceIds.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > 512))
        {
            return "The remembered sources list is not valid; choose the sources again.";
        }

        return Storage.Validate();
    }
}
