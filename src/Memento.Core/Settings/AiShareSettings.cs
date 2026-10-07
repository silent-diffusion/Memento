using System.Text.Json;
using System.Text.Json.Serialization;

namespace Memento.Core.Settings;

/// <summary>What external AI may receive when it is on (DESIGN.md §11). Audio and video are never sent.</summary>
public sealed record AiShareSettings
{
    public bool Transcript { get; set; } = true;

    public bool Details { get; set; } = true;

    public bool Participants { get; set; } = true;

    public bool Agenda { get; set; } = true;

    public bool Highlights { get; set; } = true;

    public bool Attachments { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
