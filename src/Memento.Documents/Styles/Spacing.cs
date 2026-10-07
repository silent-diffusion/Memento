using System.Text.Json.Serialization;
using Memento.Documents.Model;

namespace Memento.Documents.Styling;

/// <summary>Tight, Normal or Airy: 10 / 18 / 28 px between sections (DESIGN.md §13).</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<Spacing>))]
public enum Spacing
{
    Normal,
    Tight,
    Airy,
}
