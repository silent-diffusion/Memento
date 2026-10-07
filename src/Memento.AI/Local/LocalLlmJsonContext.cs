using System.Text.Json.Serialization;

namespace Memento.AI.Local;

/// <summary>The local job protocol and the local catalog: camelCase, nulls left out, one compact object per line.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
[JsonSerializable(typeof(LocalLlmWorkerCommand))]
[JsonSerializable(typeof(LocalLlmWorkerReply))]
[JsonSerializable(typeof(LocalModelCatalogDocument))]
public sealed partial class LocalLlmJsonContext : JsonSerializerContext
{
}
