namespace Memento.Core.Bridge.Contracts;

/// <summary>The transcript row of the Export dialog: one file per chosen format.</summary>
public sealed record ExportTranscriptChoice
{
    public bool On { get; init; }

    /// <summary>Any of <c>json</c>, <c>markdown</c>, <c>text</c>, <c>srt</c>.</summary>
    public IReadOnlyList<string> Formats { get; set; } = [];
}
