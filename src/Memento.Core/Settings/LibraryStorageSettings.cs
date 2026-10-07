using System.Text.Json;
using System.Text.Json.Serialization;

namespace Memento.Core.Settings;

/// <summary>Settings › Storage and history (M3): which recordings "Reclaim space" converts by default.</summary>
public sealed record LibraryStorageSettings
{
    public const int MinDays = 1;
    public const int MaxDays = 3650;

    /// <summary><c>null</c>: no age is set, so <c>storage.reclaim</c> needs recording ids.</summary>
    public int? ReclaimOlderThanDays { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    /// <summary>Returns the first problem, worded for people, or <c>null</c>.</summary>
    public string? Validate() =>
        ReclaimOlderThanDays is < MinDays or > MaxDays
            ? $"Recordings older than {ReclaimOlderThanDays} days is out of range. Choose {MinDays} to {MaxDays} days, or none."
            : null;
}
