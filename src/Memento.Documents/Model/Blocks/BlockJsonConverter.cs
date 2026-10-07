using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Memento.Documents.Model.Blocks;

/// <summary>
/// Reads and writes <see cref="Block"/> by its <c>type</c> discriminator using the source-generated metadata of the
/// concrete block types. Unknown types round-trip verbatim as <see cref="UnknownBlock"/>.
/// </summary>
public sealed class BlockJsonConverter : JsonConverter<Block>
{
    public override Block? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("A document block must be a JSON object.");
        }

        var type = root.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
        return type switch
        {
            BlockTypes.Heading => Deserialize<HeadingBlock>(root, options),
            BlockTypes.Paragraph => Deserialize<ParagraphBlock>(root, options),
            BlockTypes.List => Deserialize<ListBlock>(root, options),
            BlockTypes.Table => Deserialize<TableBlock>(root, options),
            BlockTypes.Chips => Deserialize<ChipsBlock>(root, options),
            BlockTypes.LabelValue => Deserialize<LabelValueBlock>(root, options),
            BlockTypes.Quote => Deserialize<QuoteBlock>(root, options),
            BlockTypes.Timeline => Deserialize<TimelineBlock>(root, options),
            BlockTypes.Transcript => Deserialize<TranscriptBlock>(root, options),
            _ => new UnknownBlock(root.Clone()),
        };
    }

    public override void Write(Utf8JsonWriter writer, Block value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        if (value is UnknownBlock unknown)
        {
            unknown.Raw.WriteTo(writer);
            return;
        }

        JsonSerializer.Serialize(writer, value, options.GetTypeInfo(value.GetType()));
    }

    private static T Deserialize<T>(JsonElement element, JsonSerializerOptions options)
        where T : Block =>
        element.Deserialize((JsonTypeInfo<T>)options.GetTypeInfo(typeof(T)))
        ?? throw new JsonException($"The {typeof(T).Name} block is empty.");
}
