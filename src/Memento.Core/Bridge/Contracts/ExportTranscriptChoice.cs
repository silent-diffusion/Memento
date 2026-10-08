namespace Memento.Core.Bridge.Contracts;

/// <summary>The transcript row of the Export dialog: one file per chosen format.</summary>
public sealed record ExportTranscriptChoice
{
    public bool On { get; init; }

    /// <summary>Any of <c>json</c>, <c>markdown</c>, <c>text</c>, <c>srt</c>.</summary>
    public IReadOnlyList<string> Formats { get; set; } = [];

    /// <summary>
    /// Timestamps, speakers and layout of the Markdown and text files (after 1.2.0). Missing or <c>null</c> in a request
    /// means the defaults; JSON and SRT keep their own structure.
    /// </summary>
    public TranscriptTextOptions? Options { get; set; } = TranscriptTextOptions.Default;
}
