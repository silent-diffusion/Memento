using System.Text.Json;
using System.Text.Json.Serialization;

namespace Memento.Documents.Model;

/// <summary>Writes enum values as camelCase strings (<c>smallCaps</c>, <c>boldItalic</c>) so the files read like the bridge.</summary>
public sealed class CamelCaseEnumConverter<TEnum> : JsonStringEnumConverter<TEnum>
    where TEnum : struct, Enum
{
    public CamelCaseEnumConverter()
        : base(JsonNamingPolicy.CamelCase, allowIntegerValues: false)
    {
    }
}
