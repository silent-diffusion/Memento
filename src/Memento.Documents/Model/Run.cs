using System.Text.Json;
using System.Text.Json.Serialization;

namespace Memento.Documents.Model;

/// <summary>One run of text. A paragraph, list item, cell or quote is a sequence of runs.</summary>
public sealed record Run
{
    public RunKind Kind { get; init; } = RunKind.Text;

    /// <summary>The text; for a timestamp, the display text ("18:42").</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Emphasis runs only.</summary>
    public EmphasisStyle? Style { get; init; }

    /// <summary>Timestamp runs only: the transcript moment in seconds.</summary>
    public double? T { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    [JsonIgnore]
    public bool IsBold => Kind == RunKind.Emphasis && Style is EmphasisStyle.Bold or EmphasisStyle.BoldItalic;

    [JsonIgnore]
    public bool IsItalic => Kind == RunKind.Emphasis && Style is EmphasisStyle.Italic or EmphasisStyle.BoldItalic;

    public static Run Plain(string text) => new() { Text = text };

    public static Run Bold(string text) => new() { Kind = RunKind.Emphasis, Style = EmphasisStyle.Bold, Text = text };

    public static Run Italic(string text) => new() { Kind = RunKind.Emphasis, Style = EmphasisStyle.Italic, Text = text };

    public static Run BoldItalic(string text) => new() { Kind = RunKind.Emphasis, Style = EmphasisStyle.BoldItalic, Text = text };

    public static Run Note(string text) => new() { Kind = RunKind.Note, Text = text };

    /// <summary>A timestamp run whose display text is the <see cref="Timecode"/> of <paramref name="seconds"/>.</summary>
    public static Run Timestamp(double seconds) => new() { Kind = RunKind.Timestamp, T = seconds, Text = Timecode.Format(seconds) };

    public static Run Timestamp(double seconds, string display) => new() { Kind = RunKind.Timestamp, T = seconds, Text = display };
}
