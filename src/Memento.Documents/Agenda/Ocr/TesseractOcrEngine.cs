namespace Memento.Documents.Agenda.Ocr;

/// <summary>
/// The open-source alternative to Windows OCR, with per-word confidence. Until the model manager installs the engine
/// and its language data (M3 wiring), it reports itself as not installed and never runs.
/// </summary>
public sealed class TesseractOcrEngine : IOcrEngine
{
    private const string NotInstalledReason =
        "Tesseract text recognition is not installed. Install it in Settings › Engines, or use Windows OCR.";

    /// <param name="languageDataDirectory">Where the model manager keeps the Tesseract language data (<c>models\tesseract</c>), when known.</param>
    public TesseractOcrEngine(string? languageDataDirectory = null)
    {
        LanguageDataDirectory = languageDataDirectory;
    }

    public string Id => "tesseract";

    public string DisplayName => "Tesseract";

    public bool ReportsConfidence => true;

    public string? LanguageDataDirectory { get; }

    public Task<OcrAvailability> GetAvailabilityAsync(string? language, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(OcrAvailability.NotInstalled(NotInstalledReason));
    }

    public Task<OcrPage> RecognizeAsync(ReadOnlyMemory<byte> image, OcrRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new AgendaImportException(AgendaErrorCodes.OcrUnavailable, NotInstalledReason + " Nothing was imported.");
    }
}
