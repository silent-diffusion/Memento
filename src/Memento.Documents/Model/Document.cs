using System.Text.Json;
using System.Text.Json.Serialization;

namespace Memento.Documents.Model;

/// <summary>
/// A document inside a recording project (<c>documents/&lt;id&gt;.json</c>, schema v1): title, meta line fields, rows of one to
/// three modules, and a reference to the generation record. Unknown fields at every level are kept on round-trip.
/// </summary>
public sealed record Document
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public string Id { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public DocumentMeta Meta { get; init; } = new();

    /// <summary>The style the document is shown and exported in.</summary>
    public string? StyleId { get; init; }

    public IReadOnlyList<DocumentRow> Rows { get; init; } = [];

    /// <summary><c>null</c> for a hand-written document.</summary>
    public GenerationReference? Generation { get; init; }

    /// <summary>Increments on every save.</summary>
    public int Version { get; init; } = 1;

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset ModifiedAt { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    /// <summary>Every module, top to bottom then left to right: the document's reading order.</summary>
    public IEnumerable<ModuleBlock> Modules() => Rows.SelectMany(r => r.Modules);
}
