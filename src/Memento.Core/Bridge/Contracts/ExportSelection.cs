namespace Memento.Core.Bridge.Contracts;

/// <summary>What the Export dialog writes (BRIDGE.md M3). Also stored as the Settings › Export defaults.</summary>
public sealed record ExportSelection
{
    /// <summary>
    /// Settings › Export defaults (DESIGN.md §11, PRODUCT-SPEC "External Export"): the mixed audio as FLAC and the
    /// transcript as JSON and Markdown; nothing else.
    /// </summary>
    public static ExportSelection Default => new()
    {
        AudioMixed = new ExportAudioChoice { On = true, Format = "flac" },
        Transcript = new ExportTranscriptChoice { On = true, Formats = ["json", "markdown"] },
    };

    public ExportAudioChoice AudioMixed { get; set; } = new();

    public ExportAudioChoice Tracks { get; set; } = new();

    public ExportTranscriptChoice Transcript { get; set; } = new();

    public ExportDocumentsChoice Documents { get; set; } = new();

    public ExportToggle Details { get; set; } = new();

    public ExportToggle Attachments { get; set; } = new();
}
