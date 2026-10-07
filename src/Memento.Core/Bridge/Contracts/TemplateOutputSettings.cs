namespace Memento.Core.Bridge.Contracts;

/// <summary>A template's output options: exports written after generation (the document is always saved in the recording).</summary>
public sealed record TemplateOutputSettings(bool AlsoExportDocx, bool AlsoExportMarkdown)
{
    public bool AlsoExportPdf { get; init; }
}
