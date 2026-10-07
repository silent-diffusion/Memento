using System.Text.Json;
using System.Text.Json.Serialization;

namespace Memento.Documents.Templates;

/// <summary>"What the AI receives" (DESIGN.md §10): the ticked set is the exact payload. Audio and video are never sent and have no switch.</summary>
public sealed record TemplateInputs
{
    /// <summary>The transcript with speakers.</summary>
    public bool Transcript { get; init; } = true;

    /// <summary>The recording details (title, date, purpose, platform, notes).</summary>
    public bool Details { get; init; } = true;

    /// <summary>The participants from the recording details.</summary>
    public bool Participants { get; init; } = true;

    public bool Agenda { get; init; } = true;

    /// <summary>Highlights and notes.</summary>
    public bool Highlights { get; init; } = true;

    /// <summary>Imported documents (attachments).</summary>
    public bool ImportedDocuments { get; init; }

    /// <summary>The recording's other documents, as text.</summary>
    public bool PreviousDocuments { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
