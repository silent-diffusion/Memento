using System.Text.Json;
using System.Text.Json.Serialization;

namespace Memento.Core.Settings;

/// <summary>Settings › Documents (M4): the template and style a new document starts from; <c>null</c> uses the built-ins.</summary>
public sealed record DocumentsSettings
{
    public string? DefaultTemplateId { get; set; }

    public string? DefaultStyleId { get; set; }

    /// <summary>
    /// 2.0, Settings › Documents › Agenda photos and scans: the text recognition tried first (<see cref="OcrEngines"/>);
    /// <c>null</c> is Windows OCR. The other engine is the fallback when the chosen one cannot run.
    /// </summary>
    public string? OcrEngine { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
