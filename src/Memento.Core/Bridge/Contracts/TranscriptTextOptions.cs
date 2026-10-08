namespace Memento.Core.Bridge.Contracts;

/// <summary>
/// How the readable transcript (Markdown and plain text) is written, in an export or on the clipboard (BRIDGE.md,
/// "Clipboard and transcript text options"). Every combination is allowed. A field that is missing keeps its default,
/// which is the transcript as Memento wrote it before these options existed. JSON and SRT ignore them.
/// </summary>
public sealed record TranscriptTextOptions
{
    /// <summary>Markdown in paragraphs per speaker turn, plain text one line per segment.</summary>
    public const string Auto = "auto";

    /// <summary>One paragraph per speaker turn: consecutive lines of the same speaker run together.</summary>
    public const string Turns = "turns";

    /// <summary>One line (Markdown: one paragraph) per transcript segment.</summary>
    public const string Lines = "lines";

    public static TranscriptTextOptions Default { get; } = new();

    public static IReadOnlyList<string> Layouts { get; } = [Auto, Turns, Lines];

    /// <summary>An <c>[h:mm:ss]</c> marker before every segment.</summary>
    public bool Timestamps { get; init; } = true;

    /// <summary>The speaker's name before each turn or line, and the speakers in the heading.</summary>
    public bool Speakers { get; init; } = true;

    /// <summary><see cref="Auto"/>, <see cref="Turns"/> or <see cref="Lines"/>.</summary>
    public string Layout { get; init; } = Auto;
}
