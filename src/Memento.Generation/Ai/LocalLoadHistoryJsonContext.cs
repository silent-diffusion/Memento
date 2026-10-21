using System.Text.Json.Serialization;

namespace Memento.Generation.Ai;

/// <summary>Source-generated serialization for <see cref="LocalLoadHistoryFile"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
[JsonSerializable(typeof(LocalLoadHistoryFile))]
internal sealed partial class LocalLoadHistoryJsonContext : JsonSerializerContext
{
}
