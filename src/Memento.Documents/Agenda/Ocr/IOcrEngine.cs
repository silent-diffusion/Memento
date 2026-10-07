namespace Memento.Documents.Agenda.Ocr;

/// <summary>
/// A text recognition engine for agenda photos. Engines run on this PC only. Windows OCR is built in; Tesseract is the
/// downloadable alternative (with per-word confidence) that the model manager installs.
/// </summary>
public interface IOcrEngine
{
    /// <summary>The engine id used in Settings: <c>windows</c> or <c>tesseract</c>.</summary>
    string Id { get; }

    /// <summary>The user-facing engine name.</summary>
    string DisplayName { get; }

    /// <summary>Whether <see cref="OcrWordBox.Confidence"/> is filled in. Windows OCR reports none.</summary>
    bool ReportsConfidence { get; }

    /// <summary>Whether the engine can run, for the given BCP-47 language or the Windows profile languages when <c>null</c>.</summary>
    Task<OcrAvailability> GetAvailabilityAsync(string? language, CancellationToken cancellationToken);

    /// <summary>
    /// Recognizes the words in an encoded image (PNG, JPEG, BMP, TIFF, HEIC). The engine prepares the image itself:
    /// small text is upscaled to <see cref="OcrRequest.MinWordHeight"/> and skewed text is straightened.
    /// </summary>
    Task<OcrPage> RecognizeAsync(ReadOnlyMemory<byte> image, OcrRequest request, CancellationToken cancellationToken);
}
