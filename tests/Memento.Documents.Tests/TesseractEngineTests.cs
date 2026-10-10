using System.Diagnostics;
using System.Globalization;
using Memento.Documents.Agenda;
using Memento.Documents.Agenda.Ocr;
using Memento.Documents.Tests.Support;
using UglyToad.PdfPig.Writer;
using Xunit.Abstractions;

namespace Memento.Documents.Tests;

/// <summary>
/// The real Tesseract in Memento.Worker on the synthetic agenda images (skipped without the worker build or the
/// language data), and the accuracy and speed comparison with Windows OCR that ENGINE-NOTES.md §P records.
/// </summary>
public sealed class TesseractEngineTests(ITestOutputHelper output)
{
    public static TheoryData<string> ImageFixtures => new(FixturePaths.All().Where(FixturePaths.IsImage));

    [TesseractTheory]
    [MemberData(nameof(ImageFixtures))]
    public async Task ImageFixtureIsReadByTesseract(string name)
    {
        var (engine, owner) = TesseractSupport.Create();
        using (owner)
        {
            var outcome = await RunAsync(name, Agendas.Fixture(name), name, engine);
            output.WriteLine(outcome.Summary);
            foreach (var item in outcome.Actual)
            {
                output.WriteLine("  " + item);
            }

            Assert.Null(outcome.Error);
            Assert.Equal("tesseract", outcome.Result!.OcrEngine);
            Assert.Contains(outcome.Result.Warnings, w => w.Code == AgendaWarningCodes.OcrReview);
            Assert.InRange(outcome.Result.Items.Count, outcome.Expected.Items.Count - 1, outcome.Expected.Items.Count + 1);
            Assert.InRange(outcome.CharacterErrorRate, 0, outcome.Expected.MaxCharacterErrorRate ?? 0.05);
        }
    }

    [TesseractFact]
    public async Task AScannedPdfIsReadByTesseract()
    {
        var (engine, owner) = TesseractSupport.Create();
        using (owner)
        {
            var result = await Agendas.ImportAsync(ScannedPdf("ocr-photo.jpg"), "scanned-agenda.pdf", Agendas.Importer(engine));

            Assert.Equal(AgendaSourceKind.Pdf, result.Source);
            Assert.Equal("tesseract", result.OcrEngine);
            Assert.Equal(AgendaWarningCodes.ScannedPdf, result.Warnings[0].Code);
            Assert.Contains("Words it was unsure about are marked", result.Warnings[0].Message, StringComparison.Ordinal);
            Assert.Contains(result.Items, i => i.Text.Contains("Seed swap", StringComparison.OrdinalIgnoreCase));
        }
    }

    [TesseractFact]
    public async Task TheWorkerMeasuresTheTextAngleOfARotatedPhoto()
    {
        var (engine, owner) = TesseractSupport.Create();
        using (owner)
        {
            var page = await engine.RecognizeAsync(Agendas.Fixture("ocr-photo.png"), new OcrRequest(), CancellationToken.None);

            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"angle {page.TextAngle}, deskewed {page.Deskewed}, scale {page.Scale:0.##}, {page.Words.Count} words"));
            Assert.NotNull(page.TextAngle);
            Assert.InRange(page.TextAngle!.Value, 2.0, 5.0);
            Assert.True(page.Deskewed);
        }
    }

    /// <summary>
    /// ENGINE-NOTES.md §P: each image fixture (and two scanned PDFs made from them) read by both engines, three times
    /// each: the character error rate, items found, items marked uncertain, and the time per import (the first run
    /// includes starting the worker and loading the language data; the median is of all three).
    /// </summary>
    [TesseractFact]
    [Trait("Category", "Hardware")]
    public async Task ComparesTesseractWithWindowsOcr()
    {
        var (tesseract, owner) = TesseractSupport.Create();
        using (owner)
        {
            var engines = new List<IOcrEngine> { tesseract };
            if (OcrSupport.IsAvailable)
            {
                engines.Insert(0, new WindowsOcrEngine());
            }

            var inputs = FixturePaths.All().Where(FixturePaths.IsImage).Select(n => (Label: n, Bytes: Agendas.Fixture(n), File: n, Expected: n)).ToList();
            inputs.Add(("scanned ocr-clean.png", ScannedPdf("ocr-clean.png"), "scan.pdf", "ocr-clean.png"));
            inputs.Add(("scanned ocr-photo.jpg", ScannedPdf("ocr-photo.jpg"), "scan.pdf", "ocr-photo.jpg"));
            var misread = new List<string>();
            output.WriteLine("| Input | Engine | CER | Items | Uncertain | First | Median |");
            output.WriteLine("|---|---|---|---|---|---|---|");
            foreach (var input in inputs)
            {
                foreach (var engine in engines)
                {
                    var runs = new List<FixtureOutcome>();
                    for (var i = 0; i < 3; i++)
                    {
                        runs.Add(await RunAsync(input.Expected, input.Bytes, input.File, engine));
                    }

                    var last = runs[^1];
                    var times = runs.Select(r => r.Elapsed.TotalMilliseconds).Order().ToList();
                    output.WriteLine(string.Create(
                        CultureInfo.InvariantCulture,
                        $"| {input.Label} | {engine.DisplayName} | {(last.Result is null ? "failed" : last.CharacterErrorRate.ToString("P1", CultureInfo.InvariantCulture))} | {last.Result?.Items.Count ?? 0}/{last.Expected.Items.Count} | {last.Result?.UncertainCount ?? 0} | {runs[0].Elapsed.TotalMilliseconds:0} ms | {times[1]:0} ms |"));
                    Assert.Null(last.Error);
                    misread.AddRange(last.CharacterErrorRate > 0 ? [$"{input.Label} · {engine.DisplayName}: {last.Result!.Title} / {string.Join(" / ", last.Actual)}"] : []);
                }
            }

            foreach (var line in misread)
            {
                output.WriteLine(line);
            }
        }
    }

    private static async Task<FixtureOutcome> RunAsync(string expectedName, byte[] bytes, string fileName, IOcrEngine engine)
    {
        var expected = ExpectedAgenda.Load(FixturePaths.Output(expectedName) + ".expected");
        var started = Stopwatch.GetTimestamp();
        try
        {
            var result = await Agendas.ImportAsync(bytes, fileName, Agendas.Importer(engine));
            return new FixtureOutcome(expectedName, expected, result, null, Stopwatch.GetElapsedTime(started));
        }
        catch (AgendaImportException e)
        {
            return new FixtureOutcome(expectedName, expected, null, e, Stopwatch.GetElapsedTime(started));
        }
    }

    /// <summary>A PDF with no text layer: the image as the only content of an A4 page.</summary>
    private static byte[] ScannedPdf(string image)
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(595, 842);
        var placement = new UglyToad.PdfPig.Core.PdfRectangle(40, 300, 555, 800);
        if (image.EndsWith(".png", StringComparison.Ordinal))
        {
            page.AddPng(Agendas.Fixture(image), placement);
        }
        else
        {
            page.AddJpeg(Agendas.Fixture(image), placement);
        }

        return builder.Build();
    }
}
