using System.Text.Json.Serialization;

namespace Memento.Core.Secrets;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SecretsDocument))]
internal sealed partial class SecretsJsonContext : JsonSerializerContext
{
}
