using Memento.Core.Workers;
using Memento.Documents.Agenda.Ocr;

namespace Memento.Documents.Tests.Support;

/// <summary>
/// A Tesseract runner that answers each image with <see cref="Answer"/> (words scaled to the image it was sent), so the
/// engine's passes, mapping and failures are tested without the worker.
/// </summary>
internal sealed class FakeTesseractRunner : ITesseractRunner
{
    /// <summary>The answer for an image; by default the given words, scaled from a 1000 px wide page to the image.</summary>
    public Func<OcrImage, OcrRecognized> Answer { get; set; } = image => new OcrRecognized(image.Id, [], null);

    /// <summary>Thrown by <see cref="OpenAsync"/> when set.</summary>
    public Exception? OpenFailure { get; set; }

    public List<OcrImage> Images { get; } = [];

    public List<(string Directory, string Language)> Opened { get; } = [];

    public int Disposed { get; private set; }

    public Task<ITesseractSession> OpenAsync(string languageDataDirectory, string language, CancellationToken cancellationToken)
    {
        if (OpenFailure is { } failure)
        {
            throw failure;
        }

        Opened.Add((languageDataDirectory, language));
        return Task.FromResult<ITesseractSession>(new Session(this));
    }

    /// <summary>Lines of words <paramref name="height"/> pixels tall in a page <paramref name="width"/> wide, scaled to each image.</summary>
    public static Func<OcrImage, OcrRecognized> Lines(double width, double height, double? angle, double confidence, params string[] lines) => image =>
    {
        var factor = image.Width / width;
        var words = FakeOcrEngine.Lines(height, lines)
            .Select(w => new OcrWord(w.Text, (int)(w.Left * factor), (int)(w.Top * factor), (int)Math.Max(1, w.Width * factor), (int)Math.Max(1, w.Height * factor), confidence))
            .ToList();
        return new OcrRecognized(image.Id, words, angle);
    };

    private sealed class Session(FakeTesseractRunner owner) : ITesseractSession
    {
        public Task<OcrRecognized> RecognizeAsync(OcrImage image, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            owner.Images.Add(image);
            return Task.FromResult(owner.Answer(image));
        }

        public ValueTask DisposeAsync()
        {
            owner.Disposed++;
            return ValueTask.CompletedTask;
        }
    }
}
