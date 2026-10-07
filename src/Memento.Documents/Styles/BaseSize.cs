using System.Text.Json.Serialization;
using Memento.Documents.Model;

namespace Memento.Documents.Styling;

/// <summary>Small, Normal or Large: 11 / 12 / 13.5 px on screen (DESIGN.md §13).</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<BaseSize>))]
public enum BaseSize
{
    Normal,
    Small,
    Large,
}
