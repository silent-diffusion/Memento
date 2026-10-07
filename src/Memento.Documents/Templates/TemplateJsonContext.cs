using System.Text.Json;
using System.Text.Json.Serialization;

namespace Memento.Documents.Templates;

/// <summary>Source-generated serialization for template files.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(DocumentTemplate))]
public sealed partial class TemplateJsonContext : JsonSerializerContext
{
}
