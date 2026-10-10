using System.Text.Json;
using System.Text.Json.Serialization;

namespace Memento.Core.Voices;

/// <summary>
/// <c>&lt;library&gt;\voices\known.json</c>, schema v1 (2.0): the voices Memento learned from names the user confirmed in
/// Review, as signatures (voice-model directions), never audio. Library-level, so it moves with the library; it never
/// leaves the PC and is not exported. Deleted by Settings › Known voices (Forget, Forget all).
/// </summary>
public sealed record KnownVoicesDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public IReadOnlyList<KnownVoice> Voices { get; init; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
