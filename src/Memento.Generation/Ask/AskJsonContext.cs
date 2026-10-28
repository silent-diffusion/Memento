using System.Text.Json.Serialization;

namespace Memento.Generation.Ask;

/// <summary>Source-generated serialization for <see cref="AskConversationFile"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
[JsonSerializable(typeof(AskConversationFile))]
internal sealed partial class AskJsonContext : JsonSerializerContext
{
}