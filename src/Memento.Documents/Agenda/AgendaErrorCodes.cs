namespace Memento.Documents.Agenda;

/// <summary>
/// The <c>agenda.*</c> codes of <see cref="AgendaImportException"/>, ready to answer a bridge call with. The message
/// of the exception is the user-facing sentence.
/// </summary>
public static class AgendaErrorCodes
{
    /// <summary>The file is larger than <see cref="AgendaLimits.MaxFileBytes"/>.</summary>
    public const string FileTooLarge = "agenda.fileTooLarge";

    /// <summary>The image is wider or taller than <see cref="AgendaLimits.MaxImageSide"/> pixels.</summary>
    public const string ImageTooLarge = "agenda.imageTooLarge";

    /// <summary>The content is not a format the importer reads (for example a legacy .doc or a PowerPoint file).</summary>
    public const string UnsupportedFormat = "agenda.unsupportedFormat";

    /// <summary>The file looks like a supported format but is damaged or truncated.</summary>
    public const string Unreadable = "agenda.unreadable";

    /// <summary>The file is password-protected.</summary>
    public const string Protected = "agenda.protected";

    /// <summary>The file holds no readable text (for example a scanned PDF with no text layer).</summary>
    public const string NoText = "agenda.noText";

    /// <summary>Text was read but no agenda items were found.</summary>
    public const string NoItems = "agenda.noItems";

    /// <summary>No text recognition engine (or language) is available for an image.</summary>
    public const string OcrUnavailable = "agenda.ocrUnavailable";
}
