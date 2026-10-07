using System.Text.Json;
using System.Text.Json.Serialization;

namespace Memento.Documents.Model.Blocks;

/// <summary>One transcript segment as placed in a document.</summary>
public sealed record TranscriptLine
{
    /// <summary>The transcript segment id (<c>s0001</c>), when known.</summary>
    public string? Id { get; init; }

    /// <summary>The speaker's display name at the time the document was composed.</summary>
    public string Speaker { get; init; } = string.Empty;

    /// <summary>Start time in seconds.</summary>
    public double T { get; init; }

    public string Text { get; init; } = string.Empty;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
