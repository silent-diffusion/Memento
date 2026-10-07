using System.Text.Json;
using System.Text.Json.Serialization;
using Memento.Documents.Model.Blocks;

namespace Memento.Documents.Model;

/// <summary>Source-generated serialization for documents. Blocks go through <see cref="BlockJsonConverter"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    Converters = [typeof(BlockJsonConverter)])]
[JsonSerializable(typeof(Document))]
[JsonSerializable(typeof(Block))]
[JsonSerializable(typeof(HeadingBlock))]
[JsonSerializable(typeof(ParagraphBlock))]
[JsonSerializable(typeof(ListBlock))]
[JsonSerializable(typeof(TableBlock))]
[JsonSerializable(typeof(ChipsBlock))]
[JsonSerializable(typeof(LabelValueBlock))]
[JsonSerializable(typeof(QuoteBlock))]
[JsonSerializable(typeof(TimelineBlock))]
[JsonSerializable(typeof(TranscriptBlock))]
[JsonSerializable(typeof(IReadOnlyList<Block>))]
public sealed partial class DocumentJsonContext : JsonSerializerContext
{
}
