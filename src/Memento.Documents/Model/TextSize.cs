using System.Text.Json.Serialization;

namespace Memento.Documents.Model;

/// <summary>A module's text size relative to the style's base size (ARCHITECTURE.md §8): 0.875, 1 or 1.15 of it.</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<TextSize>))]
public enum TextSize
{
    Normal,
    Smaller,
    Larger,
}
