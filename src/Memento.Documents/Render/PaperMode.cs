namespace Memento.Documents.Render;

/// <summary>Where a paper is shown (DESIGN.md §5.18): each mode has its own scale and padding; the markup is the same.</summary>
public enum PaperMode
{
    /// <summary>The Document viewer: real content, up to 820 px, timestamp chips that open Review.</summary>
    Viewer,

    /// <summary>The Builder preview: real title and meta line, headings in the style, grey skeleton bars per content shape.</summary>
    Skeleton,

    /// <summary>The Style editor's sample minutes at 640 px (Letter) or 620 px (A4), with running header and page number.</summary>
    Sample,

    /// <summary>The print HTML for PDF: <c>@page</c> size and margins, running header and page numbers, footnoted timestamps.</summary>
    Print,
}
