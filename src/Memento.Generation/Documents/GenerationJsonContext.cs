using System.Text.Json;
using System.Text.Json.Serialization;
using Memento.Documents.Model.Blocks;

namespace Memento.Generation.Documents;

/// <summary>Source-generated serialization for document version files (documents go through Memento.Documents' context).</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    Converters = [typeof(BlockJsonConverter)])]
[JsonSerializable(typeof(DocumentVersionFile))]
[JsonSerializable(typeof(HeadingBlock))]
[JsonSerializable(typeof(ParagraphBlock))]
[JsonSerializable(typeof(ListBlock))]
[JsonSerializable(typeof(TableBlock))]
[JsonSerializable(typeof(ChipsBlock))]
[JsonSerializable(typeof(LabelValueBlock))]
[JsonSerializable(typeof(QuoteBlock))]
[JsonSerializable(typeof(TimelineBlock))]
[JsonSerializable(typeof(TranscriptBlock))]
internal sealed partial class GenerationJsonContext : JsonSerializerContext
{
}
