namespace Memento.Core.Models;

/// <summary>What a catalog model is for (<c>ModelInfo.engine</c> on the bridge).</summary>
public static class ModelKinds
{
    public const string Transcription = "transcription";
    public const string Speakers = "speakers";
    public const string Ocr = "ocr";

    public static IReadOnlyList<string> All { get; } = [Transcription, Speakers, Ocr];
}
