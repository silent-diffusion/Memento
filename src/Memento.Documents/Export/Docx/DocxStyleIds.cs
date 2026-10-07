namespace Memento.Documents.Export.Docx;

/// <summary>The paragraph and character style ids the Word export defines (built-in names, so Word's navigation and TOC see them).</summary>
public static class DocxStyleIds
{
    public const string Normal = "Normal";
    public const string Title = "Title";
    public const string Subtitle = "Subtitle";

    /// <summary>Module headings.</summary>
    public const string Heading1 = "Heading1";

    /// <summary>Sub-headings inside a module, levels 1–3.</summary>
    public const string Heading2 = "Heading2";
    public const string Heading3 = "Heading3";
    public const string Heading4 = "Heading4";

    public const string ListParagraph = "ListParagraph";
    public const string Quote = "Quote";
    public const string TableText = "TableText";
    public const string FootnoteText = "FootnoteText";
    public const string FootnoteReference = "FootnoteReference";
    public const string Header = "Header";
    public const string Footer = "Footer";
}
