namespace Memento.Core.Bridge.Contracts;

/// <summary>Settings › Documents (M4): the template and style a new document starts from; <c>null</c> uses the built-ins.</summary>
public sealed record DocumentsSettingsSnapshot(string? DefaultTemplateId, string? DefaultStyleId)
{
    /// <summary>
    /// 2.0: the text recognition in effect for agenda photos and scans, <c>windows</c> or <c>tesseract</c>: the choice,
    /// or <c>windows</c> while Tesseract's language data is not installed.
    /// </summary>
    public string OcrEngine { get; init; } = Settings.OcrEngines.Windows;
}
