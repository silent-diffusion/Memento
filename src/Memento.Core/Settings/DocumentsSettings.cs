using System.Text.Json;
using System.Text.Json.Serialization;

namespace Memento.Core.Settings;

/// <summary>Settings › Documents (M4): the template and style a new document starts from; <c>null</c> uses the built-ins.</summary>
public sealed record DocumentsSettings
{
    public string? DefaultTemplateId { get; set; }

    public string? DefaultStyleId { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
