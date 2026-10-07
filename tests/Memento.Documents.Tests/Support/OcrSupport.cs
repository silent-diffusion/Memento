using Memento.Documents.Agenda.Ocr;

namespace Memento.Documents.Tests.Support;

/// <summary>Whether this machine has a Windows OCR language (<c>OcrEngine.AvailableRecognizerLanguages</c> is not empty).</summary>
internal static class OcrSupport
{
    private static readonly Lazy<bool> Available = new(() => WindowsOcrEngine.InstalledLanguages().Count > 0);

    public static bool IsAvailable => Available.Value;

    public const string SkipReason = "No Windows OCR language is installed (OcrEngine.AvailableRecognizerLanguages is empty).";
}
