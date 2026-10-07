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

    /// <summary>
    /// The name in the recording's Documents list ("Meeting minutes", "My notes"); <c>null</c> uses <see cref="Title"/>.
    /// A generated document is named after its template's document kind; rename changes it (M4d).
    /// </summary>
    public string? Name { get; init; }

    public DocumentMeta Meta { get; init; } = new();

    /// <summary>The style the document is shown and exported in.</summary>
    public string? StyleId { get; init; }

    public IReadOnlyList<DocumentRow> Rows { get; init; } = [];

    /// <summary><c>null</c> for a hand-written document.</summary>
    public GenerationReference? Generation { get; init; }

    /// <summary>How the content was generated (M4d): <c>null</c> for a hand-written document.</summary>
    public Records.DocumentGenerationRecord? Record { get; init; }

    /// <summary>The write that produced the current content (M4d); drives which writes keep a version.</summary>
    public DocumentChange? LastChange { get; init; }

    /// <summary>Increments on every save.</summary>
    public int Version { get; init; } = 1;

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset ModifiedAt { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    /// <summary>Every module, top to bottom then left to right: the document's reading order.</summary>
    public IEnumerable<ModuleBlock> Modules() => Rows.SelectMany(r => r.Modules);
}
