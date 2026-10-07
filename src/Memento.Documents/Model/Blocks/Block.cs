using System.Text.Json;
using System.Text.Json.Serialization;

namespace Memento.Documents.Model.Blocks;

/// <summary>
/// One block of document content (ARCHITECTURE.md §8). The JSON carries a <c>type</c> discriminator from
/// <see cref="BlockTypes"/>; a type this version does not know is kept as an <see cref="UnknownBlock"/> and written back unchanged.
/// </summary>
public abstract record Block
{
    /// <summary>The discriminator written as <c>type</c>.</summary>
    [JsonPropertyOrder(-100)]
    public abstract string Type { get; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
