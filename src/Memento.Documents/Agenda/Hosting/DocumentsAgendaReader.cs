using Memento.Core.Agendas;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Memento.Documents.Agenda.Hosting;

/// <summary>
/// Core's <see cref="IAgendaReader"/> on <see cref="AgendaImporter"/>: parses on this PC and turns an
/// <see cref="AgendaImportException"/> into the bridge error with the same <c>agenda.*</c> code and message.
/// </summary>
public sealed partial class DocumentsAgendaReader(AgendaImporter importer, ILogger<DocumentsAgendaReader>? logger = null) : IAgendaReader
{
    /// <summary>Where Windows adds a text recognition language (the <c>agenda.ocrUnavailable</c> detail).</summary>
    public const string OcrLanguageHelp =
        "Open Windows Settings › Time & language › Language & region, choose your language's options, and install \"Optical character recognition\" (or add a language that has it). Then import the image again.";

    private readonly ILogger _logger = (ILogger?)logger ?? NullLogger.Instance;

    public async Task<AgendaReading> ReadFileAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return Map(await importer.ImportFileAsync(path, null, cancellationToken).ConfigureAwait(false));
        }
        catch (AgendaImportException ex)
        {
            throw ToBridge(ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogReadFailed(_logger, ex);
            throw new BridgeException(
                DomainErrorCodes.AgendaUnreadable,
                $"{Path.GetFileName(path)} could not be opened: {(ex is UnauthorizedAccessException ? "Windows denied access to it" : "another program may be using it")}. Nothing was imported. Close it in other apps and try again, or paste the items as text.");
        }
    }

    public async Task<AgendaReading> ReadTextAsync(string text, CancellationToken cancellationToken)
    {
        try
        {
            return Map(await importer.ParseTextAsync(text, null, cancellationToken).ConfigureAwait(false));
        }
        catch (AgendaImportException ex)
        {
            throw ToBridge(ex);
        }
    }

    internal static BridgeException ToBridge(AgendaImportException exception) =>
        new(exception.Code, exception.Message, exception.Code == AgendaErrorCodes.OcrUnavailable ? OcrLanguageHelp : null);

    internal static AgendaReading Map(AgendaParseResult result) =>
        new(
            Kind(result.Source),
            result.Title,
            result.Items.Select(i => new AgendaParsedItem(i.Text, i.Uncertain, i.UncertainReason, i.Level, Location(i.Location))).ToList(),
            result.Warnings.Select(w => new AgendaWarning(w.Code, w.Message)).ToList(),
            result.OcrEngine switch
            {
                null => null,
                "windows" => "Windows OCR",
                "tesseract" => "Tesseract",
                var other => other,
            });

    internal static string Kind(AgendaSourceKind kind) => kind switch
    {
        AgendaSourceKind.PastedText => AgendaSourceKinds.PastedText,
        AgendaSourceKind.Markdown => AgendaSourceKinds.Markdown,
        AgendaSourceKind.Csv => AgendaSourceKinds.Csv,
        AgendaSourceKind.Tsv => AgendaSourceKinds.Tsv,
        AgendaSourceKind.Docx => AgendaSourceKinds.Docx,
        AgendaSourceKind.Xlsx => AgendaSourceKinds.Xlsx,
        AgendaSourceKind.Pdf => AgendaSourceKinds.Pdf,
        AgendaSourceKind.Image => AgendaSourceKinds.Image,
        _ => AgendaSourceKinds.Text,
    };

    private static string? Location(AgendaSourceLocation? location) =>
        location?.ToString() is { Length: > 0 } text ? text : null;

    [LoggerMessage(Level = LogLevel.Warning, Message = "An agenda file could not be opened")]
    private static partial void LogReadFailed(ILogger logger, Exception exception);
}
