namespace Memento.Documents.Export;

/// <summary>Codes of <see cref="DocumentExportException"/>.</summary>
public static class DocumentExportErrorCodes
{
    /// <summary>PDF was asked for but no <see cref="IPdfPrinter"/> is available.</summary>
    public const string PdfPrinterUnavailable = "documentExportPdfUnavailable";

    /// <summary>The PDF printer failed or returned nothing.</summary>
    public const string PdfFailed = "documentExportPdfFailed";

    /// <summary>Writing the file failed (disk full, folder not writable).</summary>
    public const string WriteFailed = "documentExportWriteFailed";
}
