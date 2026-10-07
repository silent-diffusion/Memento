using System.Text.Json.Serialization;
using Memento.Documents.Model;

namespace Memento.Documents.Styling;

/// <summary>Normal or Small caps module headings.</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<HeadingCase>))]
public enum HeadingCase
{
    Normal,
    SmallCaps,
}
