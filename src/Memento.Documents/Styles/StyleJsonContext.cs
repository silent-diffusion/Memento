using System.Text.Json;
using System.Text.Json.Serialization;

namespace Memento.Documents.Styling;

/// <summary>Source-generated serialization for style files.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(DocumentStyle))]
public sealed partial class StyleJsonContext : JsonSerializerContext
{
}
