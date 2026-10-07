using System.Text.Json;
using System.Text.Json.Serialization;

namespace Memento.Core.Models;

/// <summary>Source-generated serialization for the model catalog.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(ModelCatalogDocument))]
internal sealed partial class ModelJsonContext : JsonSerializerContext
{
}
