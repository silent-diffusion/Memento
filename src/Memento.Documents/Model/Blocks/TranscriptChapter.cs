using System.Text.Json;
using System.Text.Json.Serialization;

namespace Memento.Documents.Model.Blocks;

/// <summary>A chapter heading inside the Full transcript.</summary>
public sealed record TranscriptChapter
{
    public double T { get; init; }

    public string Title { get; init; } = string.Empty;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
