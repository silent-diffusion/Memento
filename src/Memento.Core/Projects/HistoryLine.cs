using System.Text.Json;
using System.Text.Json.Serialization;
using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Projects;

/// <summary>One line of <c>history.jsonl</c>. Each line carries its own schema version.</summary>
public sealed record HistoryLine
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public DateTimeOffset At { get; init; }

    public string Stage { get; init; } = string.Empty;

    public string Event { get; init; } = string.Empty;

    public string Summary { get; init; } = string.Empty;

    public string? Detail { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    public static HistoryLine From(HistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new HistoryLine { At = entry.At, Stage = entry.Stage, Event = entry.Event, Summary = entry.Summary, Detail = entry.Detail };
    }

    public HistoryEntry ToEntry() => new(At, Stage, Event, Summary, Detail);
}
