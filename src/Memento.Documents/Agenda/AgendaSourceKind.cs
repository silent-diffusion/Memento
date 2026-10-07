namespace Memento.Documents.Agenda;

/// <summary>What an agenda was read from, as detected from the content (not the file name).</summary>
public enum AgendaSourceKind
{
    /// <summary>A plain text file.</summary>
    Text,

    /// <summary>Text pasted into the agenda sheet.</summary>
    PastedText,

    Markdown,

    /// <summary>Comma- (or semicolon-) separated values.</summary>
    Csv,

    /// <summary>Tab-separated values, including cells copied from a spreadsheet.</summary>
    Tsv,

    /// <summary>A Word document (.docx).</summary>
    Docx,

    /// <summary>An Excel workbook (.xlsx).</summary>
    Xlsx,

    Pdf,

    /// <summary>A PNG, JPEG, BMP, TIFF or HEIC image read with text recognition (OCR).</summary>
    Image,
}
