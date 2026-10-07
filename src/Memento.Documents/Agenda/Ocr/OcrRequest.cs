namespace Memento.Documents.Agenda.Ocr;

/// <summary>What to recognize and how to prepare the image.</summary>
/// <param name="Language">A BCP-47 language such as <c>en-US</c>; <c>null</c> uses the Windows profile languages.</param>
/// <param name="MinWordHeight">Upscale until the median word is at least this many pixels tall.</param>
/// <param name="MaxImageSide">Refuse images wider or taller than this.</param>
/// <param name="DeskewThresholdDegrees">Straighten the image when the text is rotated by more than this.</param>
public sealed record OcrRequest(
    string? Language = null,
    int MinWordHeight = AgendaLimits.MinOcrWordHeight,
    int MaxImageSide = AgendaLimits.MaxImageSide,
    double DeskewThresholdDegrees = 1.0);
