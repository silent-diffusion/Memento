using System.Text.Json.Serialization;
using Memento.Documents.Model;

namespace Memento.Documents.Export;

/// <summary>Document export formats; the JSON names match BRIDGE.md's <c>'docx' | 'pdf' | 'markdown'</c>.</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<DocumentExportFormat>))]
public enum DocumentExportFormat
{
    Docx,
    Pdf,
    Markdown,
}
