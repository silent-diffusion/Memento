using System.Text.Json;
using System.Text.Json.Serialization;

namespace Memento.Documents.Model;

/// <summary>How a module's content was produced, for "How this was made" and the grounding validator.</summary>
public sealed record Provenance
{
    public ProvenanceKind Kind { get; init; } = ProvenanceKind.User;

    /// <summary>The generation record's claim ids behind this module (AI content only).</summary>
    public IReadOnlyList<string> ClaimIds { get; init; } = [];

    /// <summary><c>true</c> once the user has edited generated or composed content in the viewer.</summary>
    public bool Edited { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    public static Provenance FromAi(params string[] claimIds) => new() { Kind = ProvenanceKind.Ai, ClaimIds = claimIds };

    public static Provenance FromUser() => new() { Kind = ProvenanceKind.User };

    public static Provenance FromData() => new() { Kind = ProvenanceKind.Data };
}
