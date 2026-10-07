namespace Memento.Documents.Agenda.Layout;

/// <summary>A word with its box, in a top-down coordinate system (y grows downwards), from a PDF page or OCR.</summary>
/// <param name="Text">The word.</param>
/// <param name="Left">The left edge.</param>
/// <param name="Top">The top edge.</param>
/// <param name="Width">The width.</param>
/// <param name="Height">The height.</param>
/// <param name="FontSize">The font size when the source knows it (PDF).</param>
/// <param name="Bold">Whether the font is bold, when the source knows it (PDF).</param>
/// <param name="Confidence">The recognizer's confidence 0–1 when it reports one (Tesseract).</param>
internal sealed record WordBox(
    string Text,
    double Left,
    double Top,
    double Width,
    double Height,
    double? FontSize = null,
    bool Bold = false,
    double? Confidence = null)
{
    public double Right => Left + Width;

    public double Bottom => Top + Height;

    public double CenterY => Top + (Height / 2);
}
