namespace Memento.Core.Models;

/// <summary>What a catalog model is for (<c>ModelInfo.engine</c> on the bridge).</summary>
public static class ModelKinds
{
    public const string Transcription = "transcription";
    public const string Speakers = "speakers";
    public const string Ocr = "ocr";

    /// <summary>A local language model (GGUF, llama.cpp) for document generation; its <c>llm</c> block carries the run profile.</summary>
    public const string Llm = "llm";

    public static IReadOnlyList<string> All { get; } = [Transcription, Speakers, Ocr, Llm];
}
