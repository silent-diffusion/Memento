using System.Text.Json;

namespace Memento.Documents.Model.Blocks;

/// <summary>A block whose <c>type</c> this version does not know. It is kept verbatim and written back unchanged; renderers skip it.</summary>
public sealed record UnknownBlock : Block
{
    public UnknownBlock(JsonElement raw)
    {
        Raw = raw;
    }

    public JsonElement Raw { get; }

    public override string Type =>
        Raw.ValueKind == JsonValueKind.Object && Raw.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String
            ? type.GetString() ?? string.Empty
            : string.Empty;
}
