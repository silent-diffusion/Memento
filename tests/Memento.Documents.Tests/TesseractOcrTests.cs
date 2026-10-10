using Memento.Core.Models;
using Memento.Core.Workers;
using Memento.Documents.Agenda;
using Memento.Documents.Agenda.Ocr;
using Memento.Documents.Tests.Support;

namespace Memento.Documents.Tests;

/// <summary>
/// Tesseract behind the OCR interface (2.0): availability follows the model manager, the image is prepared with the
/// same passes as Windows OCR (taller words), words keep Tesseract's confidence so unsure words mark their items, and
/// worker failures become specific agenda errors. A fake runner stands in for Memento.Worker.
/// </summary>
public sealed class TesseractOcrTests
{
    private const string DataFile = @"C:\models\tesseract\eng.traineddata";

    private static FakeModelManager Installed() => new(new Dictionary<string, string> { [TesseractOcrEngine.EnglishModelId] = DataFile });

    [Fact]
    public async Task WithoutTheLanguageDataTesseractIsNotInstalled()
    {
        var engine = new TesseractOcrEngine(new FakeModelManager(), new FakeTesseractRunner());

        var availability = await engine.GetAvailabilityAsync(null, CancellationToken.None);

        Assert.False(engine.IsInstalled);
        Assert.False(availability.IsAvailable);
        Assert.Contains("Settings › Documents", availability.Reason, StringComparison.Ordinal);
        var error = await Assert.ThrowsAsync<AgendaImportException>(() => engine.RecognizeAsync(Agendas.Fixture("ocr-small.png"), new OcrRequest(), CancellationToken.None));
        Assert.Equal(AgendaErrorCodes.OcrUnavailable, error.Code);
        Assert.Contains("Nothing was imported", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("en-US", true)]
    [InlineData("en-GB", true)]
    [InlineData("en", true)]
    [InlineData("de-DE", false)]
    public async Task OnceInstalledItReadsEnglish(string? language, bool available)
    {
        var engine = new TesseractOcrEngine(Installed(), new FakeTesseractRunner());

        var availability = await engine.GetAvailabilityAsync(language, CancellationToken.None);

        Assert.Equal(available, availability.IsAvailable);
        if (!available)
        {
            Assert.Contains("English only", availability.Reason, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task WordsComeBackWithTesseractsConfidenceAndTheModelIsLeasedWhileItReads()
    {
        var models = Installed();
        var runner = new FakeTesseractRunner { Answer = FakeTesseractRunner.Lines(1000, 40, null, 0.93, "Board agenda", "1. Welcome") };
        var engine = new TesseractOcrEngine(models, runner);

        var page = await engine.RecognizeAsync(Agendas.Fixture("ocr-clean.png"), new OcrRequest(), CancellationToken.None);

        Assert.Equal("tesseract", page.EngineId);
        Assert.Equal(["Board", "agenda", "1.", "Welcome"], page.Words.Select(w => w.Text));
        Assert.All(page.Words, w => Assert.Equal(0.93, w.Confidence));
        Assert.Equal((@"C:\models\tesseract", "eng"), runner.Opened.Single());
        Assert.Equal(1, runner.Disposed);
        Assert.Equal(1, models.Leases);
        Assert.Equal(0, models.OpenLeases);
        var image = runner.Images.Single();
        Assert.Equal(image.Width * image.Height, image.Decode().Length);
    }

    [Fact]
    public async Task SmallTextIsReadAgainUpscaledToTheWordHeightTesseractPrefers()
    {
        var runner = new FakeTesseractRunner { Answer = FakeTesseractRunner.Lines(1000, 12, null, 0.9, "Team lunch agenda", "1. Menu choices") };
        var engine = new TesseractOcrEngine(Installed(), runner);

        var page = await engine.RecognizeAsync(Agendas.Fixture("ocr-small.png"), new OcrRequest(), CancellationToken.None);

        Assert.Equal(2, runner.Images.Count);
        Assert.True(runner.Images[1].Width > runner.Images[0].Width * 2, $"{runner.Images[0].Width} → {runner.Images[1].Width}");
        Assert.True(page.Scale > 2, $"Scale {page.Scale}");
        Assert.Equal(runner.Images[1].Width / (double)runner.Images[0].Width, page.Scale, 0.01);
    }

    [Fact]
    public async Task RotatedTextIsStraightenedAndReadOnceMore()
    {
        var runner = new FakeTesseractRunner { Answer = FakeTesseractRunner.Lines(1000, 40, 3.5, 0.9, "Garden committee agenda", "1. Treasurer update") };
        var engine = new TesseractOcrEngine(Installed(), runner);

        var page = await engine.RecognizeAsync(Agendas.Fixture("ocr-photo.png"), new OcrRequest(), CancellationToken.None);

        Assert.True(page.Deskewed);
        Assert.Equal(3.5, page.TextAngle);
        Assert.Equal(2, runner.Images.Count);
        Assert.True(runner.Images[1].Width > runner.Images[0].Width, "The straightened canvas is larger.");
    }

    [Fact]
    public async Task AnUnsureWordMarksItsItemThroughTheImporter()
    {
        var runner = new FakeTesseractRunner
        {
            Answer = image =>
            {
                var read = FakeTesseractRunner.Lines(1000, 40, null, 0.95, "Board agenda", "1. Welcome", "2. Budgot review")(image);
                return read with { Words = read.Words.Select(w => w.Text == "Budgot" ? w with { Confidence = 0.41 } : w).ToList() };
            },
        };
        var importer = Agendas.Importer(new TesseractOcrEngine(Installed(), runner));

        var result = await Agendas.ImportAsync(Agendas.Fixture("ocr-clean.png"), "board.png", importer);

        Assert.Equal("tesseract", result.OcrEngine);
        Assert.False(result.Items[0].Uncertain);
        Assert.True(result.Items[1].Uncertain);
        Assert.Equal(UncertainReasons.OcrLowConfidence("Budgot"), result.Items[1].UncertainReason);
        Assert.Contains(result.Warnings, w => w.Code == AgendaWarningCodes.OcrReview && w.Message.Contains("Words it was unsure about are marked", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DamagedLanguageDataIsNotUsed()
    {
        var models = Installed();
        models.Check = ModelCheck.Damaged;
        var runner = new FakeTesseractRunner();
        var engine = new TesseractOcrEngine(models, runner);

        var error = await Assert.ThrowsAsync<AgendaImportException>(() => engine.RecognizeAsync(Agendas.Fixture("ocr-clean.png"), new OcrRequest(), CancellationToken.None));

        Assert.Equal(AgendaErrorCodes.OcrUnavailable, error.Code);
        Assert.Contains("damaged or missing", error.Message, StringComparison.Ordinal);
        Assert.Empty(runner.Opened);
    }

    [Fact]
    public async Task AMissingWorkerSaysWhatToDo()
    {
        var runner = new FakeTesseractRunner { OpenFailure = new WorkerUnavailableException("Memento.Worker.exe was not found.") };
        var engine = new TesseractOcrEngine(Installed(), runner);

        var error = await Assert.ThrowsAsync<AgendaImportException>(() => engine.RecognizeAsync(Agendas.Fixture("ocr-clean.png"), new OcrRequest(), CancellationToken.None));

        Assert.Equal(AgendaErrorCodes.OcrUnavailable, error.Code);
        Assert.Contains("Memento.Worker.exe", error.Message, StringComparison.Ordinal);
        Assert.Contains("Windows OCR", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(WorkerErrorCodes.ModelLoad, AgendaErrorCodes.OcrUnavailable, "language data")]
    [InlineData(WorkerErrorCodes.Engine, AgendaErrorCodes.Unreadable, "Tesseract stopped while reading the image")]
    public async Task AFailureInTheWorkerIsASpecificError(string workerCode, string agendaCode, string words)
    {
        var runner = new FakeTesseractRunner { Answer = _ => throw new WorkerJobException(workerCode, "native failure") };
        var engine = new TesseractOcrEngine(Installed(), runner);

        var error = await Assert.ThrowsAsync<AgendaImportException>(() => engine.RecognizeAsync(Agendas.Fixture("ocr-clean.png"), new OcrRequest(), CancellationToken.None));

        Assert.Equal(agendaCode, error.Code);
        Assert.Contains(words, error.Message, StringComparison.Ordinal);
        Assert.Contains("Nothing was imported", error.Message, StringComparison.Ordinal);
        Assert.Equal(1, runner.Disposed);
    }

    [Fact]
    public async Task ACrashedWorkerIsUnreadableNotUnavailable()
    {
        var runner = new FakeTesseractRunner { Answer = _ => throw new WorkerCrashedException(unchecked((int)0xC0000005), []) };
        var engine = new TesseractOcrEngine(Installed(), runner);

        var error = await Assert.ThrowsAsync<AgendaImportException>(() => engine.RecognizeAsync(Agendas.Fixture("ocr-clean.png"), new OcrRequest(), CancellationToken.None));

        Assert.Equal(AgendaErrorCodes.Unreadable, error.Code);
    }

    [Fact]
    public async Task TheChosenEngineGoesFirstAndWindowsOcrIsTheFallback()
    {
        var windows = new FakeOcrEngine("windows", available: true, FakeOcrEngine.Lines("1. Welcome", "2. Close"));
        var tesseract = new TesseractOcrEngine(Installed(), new FakeTesseractRunner { Answer = FakeTesseractRunner.Lines(1000, 40, null, 0.9, "1. Welcome", "2. Close") });

        var chosen = await Agendas.ImportAsync(Agendas.Fixture("ocr-clean.png"), "a.png", Agendas.Importer(windows, tesseract), new AgendaParseOptions { OcrEngineId = "tesseract" });
        var notInstalled = await Agendas.ImportAsync(
            Agendas.Fixture("ocr-clean.png"),
            "a.png",
            Agendas.Importer(windows, new TesseractOcrEngine(new FakeModelManager(), new FakeTesseractRunner())),
            new AgendaParseOptions { OcrEngineId = "tesseract" });

        Assert.Equal("tesseract", chosen.OcrEngine);
        Assert.Equal("windows", notInstalled.OcrEngine);
    }

    [Fact]
    public void GreyPaperBecomesWhiteAndInkStaysDark()
    {
        // A 128 × 64 page: warm grey paper (180) darkening to 120 at the right, with a black bar (30) of "ink".
        var pixels = new byte[128 * 64];
        for (var y = 0; y < 64; y++)
        {
            for (var x = 0; x < 128; x++)
            {
                pixels[(y * 128) + x] = (byte)(x is >= 40 and < 48 && y is >= 20 and < 44 ? 30 : 180 - (60 * x / 127));
            }
        }

        var even = new GrayImage(128, 64, pixels).NormalizeBackground(16, CancellationToken.None);

        Assert.InRange(even.Pixels[(10 * 128) + 5], 245, 255);
        Assert.InRange(even.Pixels[(10 * 128) + 120], 225, 255);
        Assert.InRange(even.Pixels[(30 * 128) + 44], 0, 60);
    }

    [Fact]
    public async Task TheReaderPutsTheEngineChosenInSettingsFirst()
    {
        var folder = Directory.CreateTempSubdirectory("memento-ocr-");
        try
        {
            var store = new Memento.Core.Settings.JsonSettingsStore(Path.Combine(folder.FullName, "settings.json"), Microsoft.Extensions.Logging.Abstractions.NullLogger<Memento.Core.Settings.JsonSettingsStore>.Instance);
            var reader = new Agenda.Hosting.DocumentsAgendaReader(Agendas.Importer(), settings: store);

            Assert.Null(reader.Options());
            await store.UpdateAsync(s => s with { Documents = s.Documents with { OcrEngine = "tesseract" } }, CancellationToken.None);
            Assert.Equal("tesseract", reader.Options()!.OcrEngineId);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public void ImagesTravelAsGrayPixels()
    {
        byte[] pixels = [0, 64, 128, 255, 10, 20];

        var image = OcrImage.Create(7, 3, 2, pixels);

        Assert.Equal(pixels, image.Decode());
        Assert.Throws<ArgumentException>(() => OcrImage.Create(1, 4, 2, pixels));
        Assert.Throws<FormatException>(() => (image with { Width = 4 }).Decode());
    }
}
