using Memento.Core.Models;
using Memento.Core.Workers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Memento.Documents.Agenda.Ocr;

/// <summary>
/// The open-source alternative to Windows OCR (2.0, ENGINE-NOTES.md §P): Tesseract 5 with the English
/// <c>tessdata_fast</c> language data the model manager installs (<c>tesseract-eng</c>), run in Memento.Worker so a
/// native failure cannot reach the app. The image is decoded, upscaled and straightened here exactly as for Windows OCR
/// (<see cref="OcrPasses"/>), only to a taller word height, which Tesseract reads best; every word comes back with
/// Tesseract's confidence, so the words it was unsure of mark their items. Until the language data is installed it
/// reports itself as not installed and never runs.
/// </summary>
public sealed partial class TesseractOcrEngine : IOcrEngine
{
    /// <summary>The catalog id of the English language data.</summary>
    public const string EnglishModelId = "tesseract-eng";

    /// <summary>Tesseract's code for English.</summary>
    public const string EnglishCode = "eng";

    /// <summary>
    /// Tesseract reads best with a capital letter about 30 px tall; a median word box of 32 px (Windows OCR: 24) gets
    /// there (ENGINE-NOTES.md §P).
    /// </summary>
    public const int PreferredWordHeight = 32;

    /// <summary>The tile, in decoded pixels, over which the paper's brightness is evened out (<see cref="GrayImage.NormalizeBackground"/>).</summary>
    internal const int BackgroundTile = 32;

    /// <summary>The largest side of an image handed to Tesseract (as Windows OCR's limit).</summary>
    internal const int MaxDimension = 10_000;

    private const string NotInstalledReason =
        "Tesseract text recognition is not installed. Install Tesseract English in Settings › Documents, or use Windows OCR.";

    private readonly IModelManager? _models;
    private readonly ITesseractRunner? _runner;
    private readonly ILogger _logger;
    private int _nextImage;

    /// <summary>An engine that is never installed (no model manager or worker), as before 2.0 and in tests.</summary>
    public TesseractOcrEngine()
        : this(null, null)
    {
    }

    /// <param name="models">Where the language data is installed and verified.</param>
    /// <param name="runner">Runs Tesseract (Memento.Worker in the app).</param>
    public TesseractOcrEngine(IModelManager? models, ITesseractRunner? runner, ILogger<TesseractOcrEngine>? logger = null)
    {
        _models = models;
        _runner = runner;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    public string Id => "tesseract";

    public string DisplayName => "Tesseract";

    public bool ReportsConfidence => true;

    /// <summary>Whether the English language data is installed (and Tesseract can run here at all).</summary>
    public bool IsInstalled => _models is not null && _runner is not null && _models.IsInstalled(EnglishModelId);

    /// <summary>Tesseract's language code for a BCP-47 language, or <c>null</c> when no language data for it is offered.</summary>
    public static string? LanguageCode(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return EnglishCode;
        }

        var primary = language.Split('-', '_')[0];
        return string.Equals(primary, "en", StringComparison.OrdinalIgnoreCase) || string.Equals(primary, EnglishCode, StringComparison.OrdinalIgnoreCase)
            ? EnglishCode
            : null;
    }

    public Task<OcrAvailability> GetAvailabilityAsync(string? language, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsInstalled)
        {
            return Task.FromResult(OcrAvailability.NotInstalled(NotInstalledReason));
        }

        if (LanguageCode(language) is null)
        {
            return Task.FromResult(OcrAvailability.NotInstalled(
                $"Tesseract reads English only in this version of Memento, not {language}. Use Windows OCR with that language installed in Windows Settings."));
        }

        return Task.FromResult(OcrAvailability.Available(["en"]));
    }

    public async Task<OcrPage> RecognizeAsync(ReadOnlyMemory<byte> image, OcrRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsInstalled || _models is null || _runner is null)
        {
            throw new AgendaImportException(AgendaErrorCodes.OcrUnavailable, NotInstalledReason + " Nothing was imported.");
        }

        var language = LanguageCode(request.Language) ?? throw new AgendaImportException(
            AgendaErrorCodes.OcrUnavailable,
            $"Tesseract reads English only in this version of Memento, not {request.Language}. Nothing was imported. Use Windows OCR with that language installed in Windows Settings.");

        if (await _models.VerifyAsync(EnglishModelId, cancellationToken).ConfigureAwait(false) != ModelCheck.Verified
            || _models.Resolve(EnglishModelId) is not { } dataFile)
        {
            throw new AgendaImportException(
                AgendaErrorCodes.OcrUnavailable,
                "The Tesseract English language data is damaged or missing, so Memento set it aside. Nothing was imported. Download it again in Settings › Documents, or choose Windows OCR there.");
        }

        using var lease = _models.Use(EnglishModelId);
        var maxDimension = Math.Min(MaxDimension, request.MaxImageSide);
        var decoded = await WindowsOcrEngine.DecodeAsync(image, request.MaxImageSide, maxDimension, cancellationToken).ConfigureAwait(false);

        try
        {
            var session = await _runner.OpenAsync(Path.GetDirectoryName(dataFile)!, language, cancellationToken).ConfigureAwait(false);
            await using (session.ConfigureAwait(false))
            {
                var page = await OcrPasses.RunAsync(
                    decoded.Image.NormalizeBackground(BackgroundTile, cancellationToken),
                    decoded.Scale,
                    async (prepared, token) =>
                    {
                        var id = Interlocked.Increment(ref _nextImage);
                        var read = await session.RecognizeAsync(OcrImage.Create(id, prepared.Width, prepared.Height, prepared.Pixels), token).ConfigureAwait(false);
                        IReadOnlyList<OcrWordBox> words = read.Words.Select(w => new OcrWordBox(w.Text, w.Left, w.Top, w.Width, w.Height, w.Confidence)).ToList();
                        return (words, read.TextAngle);
                    },
                    Math.Max(request.MinWordHeight, PreferredWordHeight),
                    request.DeskewThresholdDegrees,
                    maxDimension,
                    WindowsOcrEngine.MaxImagePixels,
                    cancellationToken).ConfigureAwait(false);

                LogRecognized(_logger, page.Words.Count, page.Scale, page.TextAngle ?? 0, page.Deskewed, page.Passes);
                return new OcrPage(page.Words, page.Image.Width, page.Image.Height, page.Scale, page.TextAngle, page.Deskewed, "en", Id);
            }
        }
        catch (WorkerUnavailableException ex)
        {
            LogFailed(_logger, ex);
            throw new AgendaImportException(
                AgendaErrorCodes.OcrUnavailable,
                "Tesseract could not be started: Memento's engine helper (Memento.Worker.exe) is missing or was blocked. Nothing was imported. Choose Windows OCR in Settings › Documents, or reinstall Memento.",
                ex);
        }
        catch (WorkerJobException ex) when (ex.Code == WorkerErrorCodes.ModelLoad)
        {
            LogFailed(_logger, ex);
            throw new AgendaImportException(
                AgendaErrorCodes.OcrUnavailable,
                "Tesseract could not load its English language data. Nothing was imported. Remove Tesseract English in Settings › Documents and download it again, or choose Windows OCR there.",
                ex);
        }
        catch (Exception ex) when (ex is WorkerJobException or WorkerCrashedException)
        {
            LogFailed(_logger, ex);
            throw new AgendaImportException(
                AgendaErrorCodes.Unreadable,
                "Tesseract stopped while reading the image. Nothing was imported, and nothing else was affected. Import it again, or choose Windows OCR in Settings › Documents.",
                ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Tesseract read {WordCount} words (scale {Scale:0.##}, text angle {Angle:0.#}°, deskewed {Deskewed}, {Passes} passes)")]
    private static partial void LogRecognized(ILogger logger, int wordCount, double scale, double angle, bool deskewed, int passes);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Tesseract text recognition failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
