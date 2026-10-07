using Memento.Documents.Agenda.Ocr;

namespace Memento.Documents.Tests.Support;

/// <summary>An OCR engine that returns fixed words, so image parsing runs without Windows OCR (as on CI).</summary>
internal sealed class FakeOcrEngine(string id, bool available, IReadOnlyList<OcrWordBox>? words = null, bool reportsConfidence = false) : IOcrEngine
{
    public string Id => id;

    public string DisplayName => id;

    public bool ReportsConfidence => reportsConfidence;

    public int Calls { get; private set; }

    public TimeSpan Delay { get; init; }

    public Task<OcrAvailability> GetAvailabilityAsync(string? language, CancellationToken cancellationToken) =>
        Task.FromResult(available ? OcrAvailability.Available(["en-US"]) : OcrAvailability.NotInstalled($"{id} is not installed."));

    public async Task<OcrPage> RecognizeAsync(ReadOnlyMemory<byte> image, OcrRequest request, CancellationToken cancellationToken)
    {
        Calls++;
        if (Delay > TimeSpan.Zero)
        {
            await Task.Delay(Delay, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new OcrPage(words ?? [], 1000, 800, 1, null, false, "en-US", id);
    }

    /// <summary>Words laid out as lines of text: each line at its own height, words 12 px apart.</summary>
    public static IReadOnlyList<OcrWordBox> Lines(params string[] lines) => Lines(30, lines);

    public static IReadOnlyList<OcrWordBox> Lines(double height, params string[] lines)
    {
        var words = new List<OcrWordBox>();
        for (var l = 0; l < lines.Length; l++)
        {
            var indent = lines[l].Length - lines[l].TrimStart().Length;
            var x = 40.0 + (indent * height * 0.5);
            foreach (var word in lines[l].Trim().Split(' '))
            {
                var width = word.Length * height * 0.5;
                words.Add(new OcrWordBox(word, x, 40 + (l * height * 1.8), width, height));
                x += width + 12;
            }
        }

        return words;
    }
}
