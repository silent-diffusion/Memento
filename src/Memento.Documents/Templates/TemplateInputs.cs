using System.Text.Json;
using System.Text.Json.Serialization;

namespace Memento.Documents.Templates;

/// <summary>
/// "What the AI receives" (DESIGN.md §10): the ticked set is the exact payload. Audio and video are never sent and have no
/// switch. Setters, not init: source-generated JSON sets a missing init-only member to its default, so a file written
/// before a switch existed (the built-ins have no <c>details</c>) would read it as off.
/// </summary>
public sealed record TemplateInputs
{
    /// <summary>The transcript with speakers.</summary>
    public bool Transcript { get; set; } = true;

    /// <summary>The recording details (title, date, purpose, platform, notes).</summary>
    public bool Details { get; set; } = true;

    /// <summary>The participants from the recording details.</summary>
    public bool Participants { get; set; } = true;

    public bool Agenda { get; set; } = true;

    /// <summary>Highlights and notes.</summary>
    public bool Highlights { get; set; } = true;

    /// <summary>Imported documents (attachments).</summary>
    public bool ImportedDocuments { get; set; }

    /// <summary>The recording's other documents, as text.</summary>
    public bool PreviousDocuments { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
