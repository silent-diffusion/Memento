using System.Text.Json.Serialization;

namespace Memento.Documents.Model.Modules;

/// <summary>The kind of content a module produces (PRODUCT-SPEC "Document Modules"); the preview draws a skeleton per shape (DESIGN.md §10).</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<ContentShape>))]
public enum ContentShape
{
    Paragraph,
    List,
    Table,
    Chips,
    LabelValue,
    Quote,
    Timeline,
    Transcript,

    /// <summary>Literal text the user or the recording details supply (Title, Custom text).</summary>
    Text,
}
