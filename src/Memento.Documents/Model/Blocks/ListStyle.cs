using System.Text.Json.Serialization;

namespace Memento.Documents.Model.Blocks;

[JsonConverter(typeof(CamelCaseEnumConverter<ListStyle>))]
public enum ListStyle
{
    Bulleted,
    Numbered,
}
