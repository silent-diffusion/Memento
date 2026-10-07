using System.Text.Json;
using System.Text.Json.Serialization;
using Memento.Documents.Model.Blocks;

namespace Memento.Documents.Model;

/// <summary>
/// One module of a document: a heading plus its blocks. <see cref="Type"/> is a <see cref="Modules.ModuleIds"/> value
/// (or a type a later version added); <see cref="TextSize"/> scales its text in the viewer, Word and PDF.
/// </summary>
public sealed record ModuleBlock
{
    /// <summary>Unique within the document; the template module's id when generated from a template.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>The module type (<c>executiveSummary</c>, <c>actionItems</c>, <c>transcript</c>).</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>The heading shown above the module ("Executive summary").</summary>
    public string Title { get; init; } = string.Empty;

    public TextSize TextSize { get; init; } = TextSize.Normal;

    /// <summary>Whether the module's points carry timestamps back to the transcript.</summary>
    public bool LinkToTranscript { get; init; }

    public Provenance Provenance { get; init; } = new();

    public IReadOnlyList<Block> Blocks { get; init; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
