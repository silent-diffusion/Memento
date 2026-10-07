using System.Text.Json.Serialization;
using Memento.Documents.Model;

namespace Memento.Documents.Styling;

/// <summary>The four heading-and-rule colours of the Style editor (DESIGN.md §13); each has a paired tint for table headers.</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<HeadingColor>))]
public enum HeadingColor
{
    Navy,
    Ink,
    Forest,
    Burgundy,
}
