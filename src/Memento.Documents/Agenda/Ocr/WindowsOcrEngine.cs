using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using WinLanguage = Windows.Globalization.Language;

namespace Memento.Documents.Agenda.Ocr;

/// <summary>
/// Windows' built-in text recognition (<c>Windows.Media.Ocr</c>). It reports no confidence, so its results are always
/// shown for review. Before the final pass the image is upscaled until the median word is at least
/// <see cref="OcrRequest.MinWordHeight"/> pixels tall and straightened when the text is rotated by more than a degree
/// (ENGINE-NOTES.md §F). Only languages installed in Windows are available.
/// </summary>
public sealed partial class WindowsOcrEngine : IOcrEngine
{
    private const double MaxUpscale = 4.0;

    /// <summary>
    /// The most pixels an image is decoded or upscaled to (40 megapixels: 160 MB as BGRA). A photo within the side
    /// limit can still be 10,000 × 10,000; it is scaled down while decoding instead of being decoded in full.
    /// </summary>
    internal const double MaxImagePixels = 40_000_000;

    private readonly ILogger<WindowsOcrEngine> _logger;

    public WindowsOcrEngine(ILogger<WindowsOcrEngine>? logger = null)
    {
        _logger = logger ?? NullLogger<WindowsOcrEngine>.Instance;
    }

    public string Id => "windows";

    public string DisplayName => "Windows OCR";

    public bool ReportsConfidence => false;

    /// <summary>The BCP-47 tags of the OCR languages installed in Windows; empty when none is (as on CI images).</summary>
    public static IReadOnlyList<string> InstalledLanguages()
    {
        try
        {
            return OcrEngine.AvailableRecognizerLanguages.Select(l => l.LanguageTag).ToList();
        }
        catch (Exception e) when (e is COMException or TypeLoadException or PlatformNotSupportedException)
        {
            return [];
        }
    }

    public Task<OcrAvailability> GetAvailabilityAsync(string? language, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var installed = InstalledLanguages();
        if (installed.Count == 0)
        {
            return Task.FromResult(OcrAvailability.NotInstalled(
                "No Windows text recognition language is installed. Add one in Windows Settings › Time & language › Language & region (a language with \"Optical character recognition\"), or choose Tesseract in Settings › Engines."));
        }

        if (language is not null && CreateEngine(language) is null)
        {
            return Task.FromResult(OcrAvailability.NotInstalled(
                $"Windows text recognition for {language} is not installed. Add it in Windows Settings › Time & language › Language & region, or choose Tesseract in Settings › Engines."));
        }

        return Task.FromResult(OcrAvailability.Available(installed));
    }

    public async Task<OcrPage> RecognizeAsync(ReadOnlyMemory<byte> image, OcrRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var engine = CreateEngine(request.Language) ?? throw new AgendaImportException(
            AgendaErrorCodes.OcrUnavailable,
            "Windows text recognition is not available for this language. Nothing was imported. Add the language in Windows Settings, or choose Tesseract in Settings › Engines.");
        var maxDimension = (int)Math.Min(OcrEngine.MaxImageDimension, (uint)request.MaxImageSide);

        var original = await DecodeAsync(image, request.MaxImageSide, maxDimension, cancellationToken).ConfigureAwait(false);
        var current = original.Image;
        var scale = original.Scale;

        var result = await RunAsync(engine, current, cancellationToken).ConfigureAwait(false);
        var words = Words(result);

        // Small text: upscale so the median word is tall enough. No words at all on a small image: try twice the size once.
        var medianHeight = words.Count == 0 ? 0 : Median(words.Select(w => w.Height));
        var factor = words.Count == 0
            ? 2.0
            : medianHeight < request.MinWordHeight ? Math.Min(MaxUpscale, (request.MinWordHeight + 2) / medianHeight) : 1.0;
        factor = Math.Min(factor, MaxScale(current.Width, current.Height, maxDimension));
        if (factor > 1.15)
        {
            var upscaled = current.Scale(factor, cancellationToken);
            var second = await RunAsync(engine, upscaled, cancellationToken).ConfigureAwait(false);
            var secondWords = Words(second);
            if (secondWords.Count >= words.Count)
            {
                current = upscaled;
                result = second;
                words = secondWords;
                scale *= factor;
            }
        }

        var angle = result.TextAngle;
        var deskewed = false;
        if (angle is { } degrees && Math.Abs(degrees) > request.DeskewThresholdDegrees && words.Count > 0)
        {
            // Measured before rotating: the rotated canvas is larger than the image and is only made when it fits.
            var (rotatedWidth, rotatedHeight) = GrayImage.RotatedSize(current.Width, current.Height, -degrees);
            if (Math.Max(rotatedWidth, rotatedHeight) <= maxDimension && (long)rotatedWidth * rotatedHeight <= MaxImagePixels)
            {
                var straight = current.Rotate(-degrees, cancellationToken);
                var third = await RunAsync(engine, straight, cancellationToken).ConfigureAwait(false);
                var thirdWords = Words(third);
                if (thirdWords.Count * 10 >= words.Count * 8)
                {
                    current = straight;
                    words = thirdWords;
                    deskewed = true;
                }
            }
        }

        LogRecognized(_logger, words.Count, scale, angle ?? 0, deskewed);
        return new OcrPage(words, current.Width, current.Height, scale, angle, deskewed, engine.RecognizerLanguage.LanguageTag, Id);
    }

    private static OcrEngine? CreateEngine(string? language)
    {
        try
        {
            if (language is not null)
            {
                var requested = new WinLanguage(language);
                return OcrEngine.IsLanguageSupported(requested) ? OcrEngine.TryCreateFromLanguage(requested) : null;
            }

            return OcrEngine.TryCreateFromUserProfileLanguages() ??
                (OcrEngine.AvailableRecognizerLanguages is { Count: > 0 } languages ? OcrEngine.TryCreateFromLanguage(languages[0]) : null);
        }
        catch (Exception e) when (e is ArgumentException or COMException)
        {
            return null;
        }
    }

    /// <summary>
    /// The largest factor an image of <paramref name="width"/> × <paramref name="height"/> may be scaled by: no side over
    /// <paramref name="maxDimension"/> and no more than <see cref="MaxImagePixels"/> in all.
    /// </summary>
    internal static double MaxScale(long width, long height, int maxDimension)
    {
        if (width <= 0 || height <= 0)
        {
            return 1.0;
        }

        return Math.Min(maxDimension / (double)Math.Max(width, height), Math.Sqrt(MaxImagePixels / ((double)width * height)));
    }

    internal static async Task<(GrayImage Image, double Scale)> DecodeAsync(ReadOnlyMemory<byte> image, int maxSide, int maxDimension, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(image.ToArray(), writable: false);
        using var randomAccess = stream.AsRandomAccessStream();
        try
        {
            var decoder = await BitmapDecoder.CreateAsync(randomAccess).AsTask(cancellationToken).ConfigureAwait(false);
            var width = decoder.OrientedPixelWidth;
            var height = decoder.OrientedPixelHeight;
            if (width > maxSide || height > maxSide)
            {
                throw new AgendaImportException(
                    AgendaErrorCodes.ImageTooLarge,
                    $"The image is {width:N0} × {height:N0} pixels; images can be at most {maxSide:N0} pixels on each side. Nothing was imported. Resize the photo or crop it to the agenda, then import it again.");
            }

            // Large photos are scaled down, while decoding, to what the recognizer accepts and to at most
            // MaxImagePixels; the pixel size is applied before EXIF rotation, so the uniform factor keeps the aspect
            // ratio either way.
            var scale = Math.Min(1.0, MaxScale(width, height, maxDimension));
            var transform = new BitmapTransform { InterpolationMode = BitmapInterpolationMode.Fant };
            if (scale < 1.0)
            {
                transform.ScaledWidth = (uint)Math.Max(1, Math.Floor(decoder.PixelWidth * scale));
                transform.ScaledHeight = (uint)Math.Max(1, Math.Floor(decoder.PixelHeight * scale));
            }

            using var bitmap = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                transform,
                ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.DoNotColorManage).AsTask(cancellationToken).ConfigureAwait(false);
            if ((long)bitmap.PixelWidth * bitmap.PixelHeight > MaxImagePixels * 1.01)
            {
                throw Unreadable(null);
            }

            var buffer = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
            bitmap.CopyToBuffer(buffer.AsBuffer());
            return (GrayImage.FromPremultipliedBgra(buffer, bitmap.PixelWidth, bitmap.PixelHeight), scale);
        }
        catch (Exception e) when (e is COMException or ArgumentException or InvalidCastException)
        {
            // Windows' decoders report a damaged or truncated image (or one they have no codec for) as a COM error,
            // at any step: creating the decoder, reading its size or decoding the pixels.
            throw Unreadable(e);
        }
    }

    private static AgendaImportException Unreadable(Exception? inner) =>
        new(
            AgendaErrorCodes.Unreadable,
            "The image could not be opened; it may be damaged, or it is a HEIC photo and the HEIF Image Extensions are not installed. Nothing was imported. Save it as PNG or JPEG, or install the extensions from the Microsoft Store, then import it again.",
            inner);

    private static async Task<OcrResult> RunAsync(OcrEngine engine, GrayImage image, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(image.ToBgra().AsBuffer(), BitmapPixelFormat.Bgra8, image.Width, image.Height, BitmapAlphaMode.Premultiplied);
            return await engine.RecognizeAsync(bitmap).AsTask(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is COMException or ArgumentException)
        {
            throw new AgendaImportException(
                AgendaErrorCodes.Unreadable,
                "Windows text recognition could not read the image. Nothing was imported. Save it as PNG or JPEG at a smaller size and import it again, or paste the items as text.",
                e);
        }
    }

    private static List<OcrWordBox> Words(OcrResult result) =>
        result.Lines
            .SelectMany(l => l.Words)
            .Select(w => new OcrWordBox(w.Text, w.BoundingRect.X, w.BoundingRect.Y, w.BoundingRect.Width, w.BoundingRect.Height))
            .ToList();

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToList();
        return sorted.Count == 0 ? 0 : sorted[sorted.Count / 2];
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Windows OCR read {WordCount} words (scale {Scale:0.##}, text angle {Angle:0.#}°, deskewed {Deskewed})")]
    private static partial void LogRecognized(ILogger logger, int wordCount, double scale, double angle, bool deskewed);
}
