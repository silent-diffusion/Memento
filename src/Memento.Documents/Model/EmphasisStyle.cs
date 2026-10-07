using System.Text.Json.Serialization;

namespace Memento.Documents.Model;

/// <summary>Bold, italic or both, for <see cref="RunKind.Emphasis"/> runs.</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<EmphasisStyle>))]
public enum EmphasisStyle
{
    Bold,
    Italic,
    BoldItalic,
}
