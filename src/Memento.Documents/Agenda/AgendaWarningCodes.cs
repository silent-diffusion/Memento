namespace Memento.Documents.Agenda;

/// <summary>The codes of <see cref="AgendaParseWarning"/>.</summary>
public static class AgendaWarningCodes
{
    /// <summary>The file's extension does not match its content; the content decided how it was read.</summary>
    public const string ExtensionMismatch = "extensionMismatch";

    /// <summary>Lines that look like meeting details (date, location, attendees) or page furniture were not made items.</summary>
    public const string DetailsSkipped = "detailsSkipped";

    /// <summary>Word text boxes were not read as items.</summary>
    public const string TextBoxesIgnored = "textBoxesIgnored";

    /// <summary>A PDF page or image appears to have two columns; the left one was read first.</summary>
    public const string MultiColumn = "multiColumn";

    /// <summary>A workbook has several sheets; one was chosen.</summary>
    public const string SheetChosen = "sheetChosen";

    /// <summary>Only one table column became items; the others were left out.</summary>
    public const string ColumnsUnused = "columnsUnused";

    /// <summary>Table rows with nothing in the agenda column; their other cells are in the warning content.</summary>
    public const string RowsWithoutItem = "rowsWithoutItem";

    /// <summary>A table that does not look like an agenda was not read; its text is in the warning content.</summary>
    public const string TableSkipped = "tableSkipped";

    /// <summary>The file was not UTF-8 and was read with a fallback encoding.</summary>
    public const string EncodingFallback = "encodingFallback";

    /// <summary>Text recognition was used; the items need a check against the original.</summary>
    public const string OcrReview = "ocrReview";

    /// <summary>More items than an agenda can hold; the rest are in the warning content.</summary>
    public const string TooManyItems = "tooManyItems";

    /// <summary>Some text could not be placed in the list; it is in the warning content.</summary>
    public const string Unparsed = "unparsed";

    /// <summary>A PDF without a text layer was rendered and read with text recognition (M3).</summary>
    public const string ScannedPdf = "scannedPdf";
}
