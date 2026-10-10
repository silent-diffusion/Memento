namespace Memento.Core.Workers;

/// <summary>A <c>recognized</c> line: what Tesseract read in one <see cref="OcrImage"/>.</summary>
/// <param name="Id">The image's <see cref="OcrImage.Id"/>.</param>
/// <param name="Words">The words in reading order as Tesseract found them.</param>
/// <param name="TextAngle">How far the text is rotated, in degrees clockwise, when Tesseract measured it.</param>
public sealed record OcrRecognized(int Id, IReadOnlyList<OcrWord> Words, double? TextAngle);
