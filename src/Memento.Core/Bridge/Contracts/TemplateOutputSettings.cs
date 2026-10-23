namespace Memento.Core.Bridge.Contracts;

/// <summary>A template's output options: exports written after generation (the document is always saved in the recording).</summary>
public sealed record TemplateOutputSettings(bool AlsoExportDocx, bool AlsoExportMarkdown)
{
    /// <summary>A template that names no output options: the document is only saved in the recording.</summary>
    public static TemplateOutputSettings None { get; } = new(false, false);

    public bool AlsoExportPdf { get; init; }
}
