namespace Memento.Core.Projects;

/// <summary>
/// Processing stage names in pipeline order. M1 runs <see cref="Stored"/> and, when Settings › Recording asks for a
/// smaller format, <see cref="Optimize"/> after every other stage (ARCHITECTURE.md §5, "Storage format options").
/// </summary>
public static class StageNames
{
    public const string Stored = "stored";
    public const string Transcript = "transcript";
    public const string Speakers = "speakers";

    /// <summary>Local keyword topics after a transcript (M2 clarification 1); finished, it is left out of rows like <c>stored</c>.</summary>
    public const string Topics = "topics";
    public const string Minutes = "minutes";

    /// <summary>Converts the lossless FLAC files to the chosen lossy format; always the last stage.</summary>
    public const string Optimize = "optimize";
}
