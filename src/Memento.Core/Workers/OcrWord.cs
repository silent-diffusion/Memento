namespace Memento.Core.Workers;

/// <summary>A word Tesseract recognized, with its box in the image's pixels (y grows downwards).</summary>
/// <param name="Confidence">Tesseract's confidence for the word, 0–1.</param>
public sealed record OcrWord(string Text, int Left, int Top, int Width, int Height, double Confidence);
