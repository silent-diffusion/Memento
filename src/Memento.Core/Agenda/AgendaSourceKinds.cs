namespace Memento.Core.Agendas;

/// <summary>Values of <c>AgendaParsePreview.sourceKind</c> (BRIDGE.md M3).</summary>
public static class AgendaSourceKinds
{
    public const string Text = "text";
    public const string PastedText = "pastedText";
    public const string Markdown = "markdown";
    public const string Csv = "csv";
    public const string Tsv = "tsv";
    public const string Docx = "docx";
    public const string Xlsx = "xlsx";
    public const string Pdf = "pdf";
    public const string Image = "image";

    public static IReadOnlyList<string> All { get; } = [Text, PastedText, Markdown, Csv, Tsv, Docx, Xlsx, Pdf, Image];

    public static bool IsValid(string? kind) => kind is not null && All.Contains(kind, StringComparer.Ordinal);

    /// <summary>The kind in words, for History: "a Word document".</summary>
    public static string Describe(string kind) => kind switch
    {
        Docx => "a Word document",
        Xlsx => "an Excel workbook",
        Pdf => "a PDF",
        Image => "an image, with text recognition",
        Csv => "a CSV file",
        Tsv => "a TSV file",
        Markdown => "a Markdown file",
        PastedText => "pasted text",
        _ => "a text file",
    };
}
