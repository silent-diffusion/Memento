using System.Text.Json;
using System.Text.Json.Serialization;

namespace Memento.Documents.Model.Records;

/// <summary>One request to the provider: what it was for, the SHA-256 of its canonical form, tokens and the stop reason.</summary>
public sealed record RecordRequest
{
    /// <summary><c>map.decisions#2</c>, <c>verify.claim</c>.</summary>
    public string Purpose { get; init; } = string.Empty;

    public string Hash { get; init; } = string.Empty;

    public int InputTokens { get; init; }

    public int OutputTokens { get; init; }

    public string? StopReason { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
