using System.Globalization;

namespace Memento.Documents.Agenda;

/// <summary>Builds the <see cref="AgendaImportException"/>s with their user-facing messages (DESIGN.md §17).</summary>
internal static class AgendaErrors
{
    public static string DisplayName(AgendaParseOptions options) =>
        string.IsNullOrWhiteSpace(options.FileName) ? "The agenda" : Path.GetFileName(options.FileName.Trim());

    public static AgendaImportException FileTooLarge(AgendaParseOptions options, long? bytes) =>
        new(
            AgendaErrorCodes.FileTooLarge,
            bytes is { } size
                ? $"{DisplayName(options)} is {Megabytes(size)}, larger than the {Megabytes(options.MaxFileBytes)} limit for an agenda. Nothing was imported. Save just the agenda pages in a smaller file, or paste the items as text."
                : $"{DisplayName(options)} is larger than the {Megabytes(options.MaxFileBytes)} limit for an agenda. Nothing was imported. Save just the agenda pages in a smaller file, or paste the items as text.");

    /// <summary>A Word or Excel package that would unpack to more than the agenda limits (a ZIP bomb, or simply far too big).</summary>
    public static AgendaImportException PackageTooLarge(AgendaParseOptions options, string what, string detail) =>
        new(
            AgendaErrorCodes.FileTooLarge,
            $"{DisplayName(options)} is {what} that {detail}, more than Memento unpacks for an agenda. Nothing was imported. Copy the agenda into a new document and import that, or paste the items as text.");

    public static AgendaImportException ImageTooLarge(AgendaParseOptions options, long width, long height) =>
        new(
            AgendaErrorCodes.ImageTooLarge,
            string.Create(
                CultureInfo.InvariantCulture,
                $"{DisplayName(options)} is {width:N0} × {height:N0} pixels; images can be at most {options.MaxImageSide:N0} pixels on each side. Nothing was imported. Resize the photo or crop it to the agenda, then import it again."));

    public static AgendaImportException Unsupported(AgendaParseOptions options, string what, string fix) =>
        new(AgendaErrorCodes.UnsupportedFormat, $"{DisplayName(options)} is {what}, which Memento cannot read as an agenda. Nothing was imported. {fix}");

    public static AgendaImportException Unreadable(AgendaParseOptions options, string what, Exception? inner = null) =>
        new(
            AgendaErrorCodes.Unreadable,
            $"{DisplayName(options)} looks like {what} but could not be read; it may be damaged or incomplete. Nothing was imported. Open it in the app that made it and save it again, or paste the items as text.",
            inner);

    /// <summary>A file whose structure no real document has (a DTD in a Word part, nesting thousands deep).</summary>
    public static AgendaImportException Malformed(AgendaParseOptions options, string what, string why, Exception? inner = null) =>
        new(
            AgendaErrorCodes.Unreadable,
            $"{DisplayName(options)} looks like {what} but {why}, which Word and Excel never write, so it was not read. Nothing was imported. Open it in the app that made it and save it again, or paste the items as text.",
            inner);

    public static AgendaImportException Protected(AgendaParseOptions options, Exception? inner = null) =>
        new(
            AgendaErrorCodes.Protected,
            $"{DisplayName(options)} is password-protected. Nothing was imported. Save a copy without the password, or paste the items as text.",
            inner);

    public static AgendaImportException NoText(AgendaParseOptions options, string why, string fix) =>
        new(AgendaErrorCodes.NoText, $"{DisplayName(options)} {why}. Nothing was imported. {fix}");

    public static AgendaImportException NoItems(AgendaParseOptions options) =>
        new(
            AgendaErrorCodes.NoItems,
            $"No agenda items were found in {Lower(DisplayName(options))}. Nothing was imported. Paste the items as text or add them by hand.");

    public static AgendaImportException OcrUnavailable(AgendaParseOptions options, string reason) =>
        new(
            AgendaErrorCodes.OcrUnavailable,
            $"{DisplayName(options)} is an image, and no text recognition is available to read it: {reason} Nothing was imported.");

    private static string Lower(string name) => name == "The agenda" ? "the agenda" : name;

    private static string Megabytes(long bytes) =>
        string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024.0):0.#} MB");
}
