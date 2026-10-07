using System.Text.Json.Serialization;

namespace Memento.Documents.Model.Modules;

/// <summary>The Builder palette's groups (DESIGN.md §10).</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<PaletteGroup>))]
public enum PaletteGroup
{
    Structure,
    Detail,
    Custom,
}
