using System.Text.Json.Serialization;
using Memento.Documents.Model;

namespace Memento.Documents.Styling;

/// <summary>Letter (8.5 × 11 in) or A4 (210 × 297 mm).</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<PaperSize>))]
public enum PaperSize
{
    Letter,
    A4,
}
