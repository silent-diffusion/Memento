using System.Text.Json;
using System.Text.Json.Serialization;

namespace Memento.Documents.Model;

/// <summary>
/// Points at the generation record that produced a document (M4d writes the record itself) and repeats what
/// "How this was made" shows first: template, style, provider and when. <c>null</c> on a hand-written document.
/// </summary>
public sealed record GenerationReference
{
    /// <summary>The generation record's id inside the project.</summary>
    public string RecordId { get; init; } = string.Empty;

    public string? TemplateId { get; init; }

    public string? StyleId { get; init; }

    public string? ProviderId { get; init; }

    public DateTimeOffset? GeneratedAt { get; init; }

    public long? DurationMs { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
