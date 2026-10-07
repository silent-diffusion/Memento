namespace Memento.Documents.Agenda.Ocr;

/// <summary>What an OCR engine recognized in one image.</summary>
/// <param name="Words">The words with their boxes in the prepared image.</param>
/// <param name="Width">The prepared image's width in pixels (after scaling and straightening).</param>
/// <param name="Height">The prepared image's height in pixels.</param>
/// <param name="Scale">How much the original was scaled (2 means upscaled to twice its size).</param>
/// <param name="TextAngle">The text rotation the engine measured in the original, in degrees clockwise, when it reports one.</param>
/// <param name="Deskewed">The image was rotated by <c>-TextAngle</c> before the final recognition.</param>
/// <param name="Language">The BCP-47 language used.</param>
/// <param name="EngineId">The engine that produced this page.</param>
public sealed record OcrPage(
    IReadOnlyList<OcrWordBox> Words,
    int Width,
    int Height,
    double Scale,
    double? TextAngle,
    bool Deskewed,
    string Language,
    string EngineId);
