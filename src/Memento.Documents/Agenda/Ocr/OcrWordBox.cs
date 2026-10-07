namespace Memento.Documents.Agenda.Ocr;

/// <summary>A recognized word and its box in the prepared image's pixels (y grows downwards).</summary>
/// <param name="Text">The word as recognized.</param>
/// <param name="Left">The left edge in pixels.</param>
/// <param name="Top">The top edge in pixels.</param>
/// <param name="Width">The width in pixels.</param>
/// <param name="Height">The height in pixels.</param>
/// <param name="Confidence">0–1 when the engine reports confidence (Tesseract); <c>null</c> for Windows OCR.</param>
public sealed record OcrWordBox(string Text, double Left, double Top, double Width, double Height, double? Confidence = null);
