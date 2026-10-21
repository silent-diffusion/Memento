namespace Memento.Core.Bridge.Contracts;

/// <summary>One model of the catalog with its installed state (<c>models.list</c>).</summary>
/// <param name="Engine">What it is for: <c>transcription</c>, <c>speakers</c> or <c>ocr</c>.</param>
/// <param name="Installing"><c>null</c> unless it is downloading.</param>
/// <param name="Recommended">The model to pick on this PC (the most accurate one that fits).</param>
/// <param name="RunsOn"><c>gpu</c>, <c>cpu</c> or <c>either</c>.</param>
/// <param name="AccuracyNote">"Most accurate", "Fast on CPU".</param>
/// <param name="Role">
/// For speaker models: <c>segmentation</c> (always needed, not a choice) or <c>embedding</c> (the voice model Settings
/// › Speakers chooses); <c>null</c> for other engines.
/// </param>
public sealed record ModelInfo(
    string Id,
    string Engine,
    string Name,
    string Description,
    long SizeBytes,
    string License,
    bool Installed,
    ModelInstalling? Installing,
    bool Recommended,
    string RunsOn,
    long? MinVramBytes,
    string AccuracyNote,
    string? Role)
{
    /// <summary>After 2.2, local language models: <c>larger</c> for the models split between the graphics card and RAM (Settings' "Larger models"); <c>null</c> otherwise.</summary>
    public string? Group { get; init; }

    /// <summary>After 2.2, larger models: the memory (RAM and video memory together) the model needs at its smallest context; <c>null</c> otherwise.</summary>
    public long? MinRamBytes { get; init; }
}
