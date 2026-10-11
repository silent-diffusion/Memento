namespace Memento.Documents.Agenda.Ocr;

/// <summary>
/// The passes every engine makes over an agenda image (ENGINE-NOTES.md §F, §P): read it as decoded; when the median word
/// is shorter than the engine wants (or nothing was found), read it again upscaled and keep that when it found at least
/// as many words; when the text is rotated by more than the threshold, read it once more straightened and keep that
/// when it found at least 80% of the words. The engine supplies one recognition and the size it prefers.
/// </summary>
internal static class OcrPasses
{
    /// <summary>Text is never upscaled by more than this.</summary>
    public const double MaxUpscale = 4.0;

    /// <summary>One recognition of a prepared image: the words in its pixels and the text angle when the engine measured one.</summary>
    public delegate Task<(IReadOnlyList<OcrWordBox> Words, double? TextAngle)> Recognize(GrayImage image, CancellationToken cancellationToken);

    /// <summary>The page the passes kept.</summary>
    /// <param name="Passes">How many recognitions ran (1 to 3).</param>
    public sealed record Outcome(GrayImage Image, IReadOnlyList<OcrWordBox> Words, double Scale, double? TextAngle, bool Deskewed, int Passes);

    /// <param name="original">The decoded image.</param>
    /// <param name="scale">How much decoding already scaled it (below 1 for a large photo).</param>
    /// <param name="minWordHeight">Upscale until the median word is at least this many pixels tall.</param>
    /// <param name="maxDimension">No side may exceed this, before or after rotating.</param>
    public static async Task<Outcome> RunAsync(
        GrayImage original,
        double scale,
        Recognize recognize,
        int minWordHeight,
        double deskewThresholdDegrees,
        int maxDimension,
        double maxPixels,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(recognize);
        var current = original;
        var (words, angle) = await recognize(current, cancellationToken).ConfigureAwait(false);
        var passes = 1;

        // Small text: upscale so the median word is tall enough. No words at all on a small image: try twice the size once.
        var medianHeight = words.Count == 0 ? 0 : Median(words.Select(w => w.Height));
        var factor = words.Count == 0
            ? 2.0
            : medianHeight < minWordHeight ? Math.Min(MaxUpscale, (minWordHeight + 2) / medianHeight) : 1.0;
        factor = Math.Min(factor, MaxScale(current.Width, current.Height, maxDimension, maxPixels));
        if (factor > 1.15)
        {
            var upscaled = current.Scale(factor, cancellationToken);
            var (secondWords, secondAngle) = await recognize(upscaled, cancellationToken).ConfigureAwait(false);
            passes++;
            if (secondWords.Count >= words.Count)
            {
                current = upscaled;
                words = secondWords;
                angle = secondAngle;
                scale *= factor;
            }
        }

        var deskewed = false;
        if (angle is { } degrees && Math.Abs(degrees) > deskewThresholdDegrees && words.Count > 0)
        {
            // Measured before rotating: the rotated canvas is larger than the image and is only made when it fits.
            var (rotatedWidth, rotatedHeight) = GrayImage.RotatedSize(current.Width, current.Height, -degrees);
            if (Math.Max(rotatedWidth, rotatedHeight) <= maxDimension && (long)rotatedWidth * rotatedHeight <= maxPixels)
            {
                var straight = current.Rotate(-degrees, cancellationToken);
                var (thirdWords, _) = await recognize(straight, cancellationToken).ConfigureAwait(false);
                passes++;
                if (thirdWords.Count * 10 >= words.Count * 8)
                {
                    current = straight;
                    words = thirdWords;
                    deskewed = true;
                }
            }
        }

        return new Outcome(current, words, scale, angle, deskewed, passes);
    }

    /// <summary>
    /// The largest factor an image of <paramref name="width"/> × <paramref name="height"/> may be scaled by: no side over
    /// <paramref name="maxDimension"/> and no more than <paramref name="maxPixels"/> in all.
    /// </summary>
    public static double MaxScale(long width, long height, int maxDimension, double maxPixels)
    {
        if (width <= 0 || height <= 0)
        {
            return 1.0;
        }

        return Math.Min(maxDimension / (double)Math.Max(width, height), Math.Sqrt(maxPixels / ((double)width * height)));
    }

    public static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToList();
        return sorted.Count == 0 ? 0 : sorted[sorted.Count / 2];
    }
}
