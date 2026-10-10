using System.Text.Json;
using System.Text.Json.Serialization;

namespace Memento.Core.Voices;

/// <summary>Source-generated serialization for <c>voices/known.json</c>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(KnownVoicesDocument))]
[JsonSerializable(typeof(KnownVoice))]
[JsonSerializable(typeof(System.Text.Json.Nodes.JsonNode))]
internal sealed partial class KnownVoicesJsonContext : JsonSerializerContext
{
}
