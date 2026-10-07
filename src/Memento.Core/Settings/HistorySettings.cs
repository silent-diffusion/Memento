using System.Text.Json;
using System.Text.Json.Serialization;

namespace Memento.Core.Settings;

/// <summary>Settings › Documents › History (M2): keep earlier versions of transcripts, and for how long.</summary>
public sealed record HistorySettings
{
    public const int DefaultKeepDays = 90;
    public const int MinKeepDays = 1;
    public const int MaxKeepDays = 3650;

    public bool KeepVersions { get; init; } = true;

    public int KeepDays { get; init; } = DefaultKeepDays;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    /// <summary>Returns the first problem, worded for people, or <c>null</c>.</summary>
    public string? Validate() =>
        KeepDays is < MinKeepDays or > MaxKeepDays
            ? $"Keeping versions for {KeepDays} days is out of range. Choose {MinKeepDays} to {MaxKeepDays} days."
            : null;
}
