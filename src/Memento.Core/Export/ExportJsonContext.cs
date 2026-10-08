using System.Text.Json.Serialization;

namespace Memento.Core.Export;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(ExportManifestDocument))]
[JsonSerializable(typeof(ExportJournalDocument))]
internal sealed partial class ExportJsonContext : JsonSerializerContext
{
}
