using System.Text.Json;

namespace Memento.Core.Bridge.Contracts;

/// <summary>Settings › Documents for <c>settings.set</c> (M4): an id, or <c>null</c> for the built-in; omitted keeps it.</summary>
public sealed record DocumentsSettingsPatch
{
    public JsonElement DefaultTemplateId { get; init; }

    public JsonElement DefaultStyleId { get; init; }

    /// <summary>2.0: <c>windows</c> or <c>tesseract</c> (installed), or <c>null</c> for Windows OCR; omitted keeps it.</summary>
    public JsonElement OcrEngine { get; init; }
}
