using System.Text.Json;
using System.Text.Json.Serialization;
using Memento.Documents.Model;

namespace Memento.Generation.Documents;

/// <summary>
/// <c>versions/document.&lt;documentId&gt;.&lt;utc-stamp&gt;.json</c> (schema v1): a document's content as it was before a
/// write replaced it, and how that content had come to be (<see cref="Reason"/>).
/// </summary>
public sealed record DocumentVersionFile
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>The UTC stamp, which is also the version id (<c>20261007T101500123Z</c>).</summary>
    public string Id { get; init; } = string.Empty;

    public string DocumentId { get; init; } = string.Empty;

    /// <summary>When the kept content was saved.</summary>
    public DateTimeOffset SavedAt { get; init; }

    /// <summary>How the kept content came to be: <c>generated</c>, <c>edited</c>, <c>restored</c> or <c>regenerated</c>.</summary>
    public string Reason { get; init; } = string.Empty;

    public Document Document { get; init; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
