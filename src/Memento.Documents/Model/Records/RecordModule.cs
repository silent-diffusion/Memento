using System.Text.Json;
using System.Text.Json.Serialization;

namespace Memento.Documents.Model.Records;

/// <summary>One module's checks: candidate claims, how many the verifier supported, how many were dropped, "not discussed".</summary>
public sealed record RecordModule
{
    public string ModuleId { get; init; } = string.Empty;

    public string Type { get; init; } = string.Empty;

    /// <summary><c>ai</c> (generated and verified), <c>data</c> (placed without AI) or <c>user</c>.</summary>
    public string Source { get; init; } = string.Empty;

    public int Claims { get; init; }

    public int Verified { get; init; }

    public int Dropped { get; init; }

    public bool NotDiscussed { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
