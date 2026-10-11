namespace Memento.Core.Settings;

/// <summary>
/// Allowed values of <see cref="DocumentsSettings.OcrEngine"/>: the text recognition that reads agenda photos and scanned
/// PDFs (2.0). Windows OCR is built in and the default; Tesseract can be chosen once its language data is installed.
/// </summary>
public static class OcrEngines
{
    public const string Windows = "windows";
    public const string Tesseract = "tesseract";

    /// <summary>The model manager's catalog id of Tesseract's English language data.</summary>
    public const string TesseractEnglishModelId = "tesseract-eng";

    public static IReadOnlyList<string> All { get; } = [Windows, Tesseract];

    public static bool IsValid(string? value) => value is not null && All.Contains(value, StringComparer.Ordinal);
}
