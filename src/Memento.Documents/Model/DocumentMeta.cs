using System.Text.Json;
using System.Text.Json.Serialization;

namespace Memento.Documents.Model;

/// <summary>
/// The fields of the meta line under the title ("Meeting minutes · Sunday 5 October 2026, 4:00 PM · 1 h 10 min · Zoom")
/// and of the running header (recording title and date). <see cref="MetaLine"/> formats them.
/// </summary>
public sealed record DocumentMeta
{
    /// <summary>What the document is ("Meeting minutes"), usually the template's document kind.</summary>
    public string? Kind { get; init; }

    /// <summary>When the recording started, with the offset it was made in.</summary>
    public DateTimeOffset? RecordedAt { get; init; }

    public long? DurationMs { get; init; }

    /// <summary>Where it took place ("Zoom", "Room 4").</summary>
    public string? Platform { get; init; }

    public int? ParticipantCount { get; init; }

    /// <summary>The recording's title, for the running header; falls back to the document title.</summary>
    public string? RecordingTitle { get; init; }

    public string? RecordingId { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
