using System.Text.Json;
using System.Text.Json.Serialization;

namespace Memento.Documents.Templates;

/// <summary>Output options. The document is always saved inside the recording; these add exports after generation.</summary>
public sealed record TemplateOutput
{
    public bool AlsoExportDocx { get; init; }

    public bool AlsoExportMarkdown { get; init; }

    public bool AlsoExportPdf { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
