using System.Drawing;
using System.Drawing.Imaging;
using Memento.Documents.Agenda;
using Memento.Documents.Agenda.Ocr;
using Memento.Documents.Tests.Support;

namespace Memento.Documents.Tests;

/// <summary>Image parsing with a fake engine (runs everywhere), the image helpers, and Windows OCR itself where installed.</summary>
public sealed class OcrTests
{
    [Theory]
    [InlineData("Budget S45,DDO approved", "S45,DDO")]
    [InlineData("Plan for 2O31", "2O31")]
    [InlineData("Start at 1O:30", "1O:30")]
    [InlineData("Budget | review", "|")]
    public void SpotsCharactersTextRecognitionProbablyMisread(string line, string token) =>
        Assert.Equal(token, OcrTextChecks.FindSuspicious(line));

    [Theory]
    [InlineData("Q3 review of B2B sales")]
    [InlineData("Lunch at 10am, 12:30 or 2031")]
    [InlineData("COVID-19 guidance and 5G rollout")]
    public void LeavesOrdinaryLettersAndDigitsAlone(string line) => Assert.Null(OcrTextChecks.FindSuspicious(line));

    [Fact]
    public async Task AnImageIsReadThroughTheTextRules()
    {
        var engine = new FakeOcrEngine("fake", available: true, FakeOcrEngine.Lines("Board agenda", "1. Welcome", "2. Budget S45,DDO", "   - Detail", "3. Close"));

        var result = await Agendas.ImportAsync(Agendas.Fixture("ocr-small.png"), "board.png", Agendas.Importer(engine));

        Assert.Equal("Board agenda", result.Title);
        Assert.Equal(["0|Welcome", "0|Budget S45,DDO?", "1|Detail", "0|Close"], Agendas.Shape(result));
        Assert.Equal(UncertainReasons.OcrSuspicious("S45,DDO"), result.Items[1].UncertainReason);
        Assert.Equal("fake", result.OcrEngine);
        Assert.Contains(result.Warnings, w => w.Code == AgendaWarningCodes.OcrReview);
        Assert.Equal(2, result.Items[0].Location.Line);
    }

    [Fact]
    public async Task ALowConfidenceWordMarksItsItem()
    {
        var words = FakeOcrEngine.Lines("1. Welcome", "2. Budget")
            .Select(w => w with { Confidence = w.Text == "Budget" ? 0.3 : 0.95 })
            .ToList();
        var engine = new FakeOcrEngine("tesseract", available: true, words, reportsConfidence: true);

        var result = await Agendas.ImportAsync(Agendas.Fixture("ocr-small.png"), "a.png", Agendas.Importer(engine));

        Assert.Equal(UncertainReasons.OcrLowConfidence("Budget"), result.Items[1].UncertainReason);
        Assert.False(result.Items[0].Uncertain);
    }

    [Fact]
    public async Task ThePreferredEngineIsUsedWhenAvailableAndTheOtherOtherwise()
    {
        var windows = new FakeOcrEngine("windows", available: true, FakeOcrEngine.Lines("1. Welcome", "2. Close"));
        var tesseract = new FakeOcrEngine("tesseract", available: true, FakeOcrEngine.Lines("1. Welcome", "2. Close"));
        var importer = Agendas.Importer(windows, tesseract);

        var preferred = await Agendas.ImportAsync(Agendas.Fixture("ocr-small.png"), "a.png", importer, new AgendaParseOptions { OcrEngineId = "tesseract" });
        Assert.Equal("tesseract", preferred.OcrEngine);

        var fallback = await Agendas.ImportAsync(
            Agendas.Fixture("ocr-small.png"),
            "a.png",
            Agendas.Importer(new FakeOcrEngine("tesseract", available: false), windows),
            new AgendaParseOptions { OcrEngineId = "tesseract" });
        Assert.Equal("windows", fallback.OcrEngine);
    }

    [Fact]
    public async Task WithNoEngineAvailableTheErrorSaysWhatToInstall()
    {
        var importer = AgendaImporter.CreateDefault([new TesseractOcrEngine()]);

        var error = await Assert.ThrowsAsync<AgendaImportException>(() => Agendas.ImportAsync(Agendas.Fixture("ocr-small.png"), "photo.png", importer));

        Assert.Equal(AgendaErrorCodes.OcrUnavailable, error.Code);
        Assert.Contains("Settings › Engines", error.Message, StringComparison.Ordinal);
        Assert.Contains("Nothing was imported", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnImageWithNoTextIsASpecificError()
    {
        var engine = new FakeOcrEngine("fake", available: true, []);

        var error = await Assert.ThrowsAsync<AgendaImportException>(() => Agendas.ImportAsync(Agendas.Fixture("ocr-small.png"), "blank.png", Agendas.Importer(engine)));

        Assert.Equal(AgendaErrorCodes.NoText, error.Code);
    }

    [Fact]
    public async Task TesseractReportsNotInstalledAndKeepsRoomForConfidence()
    {
        var tesseract = new TesseractOcrEngine();

        var availability = await tesseract.GetAvailabilityAsync("en-US", CancellationToken.None);

        Assert.False(availability.IsAvailable);
        Assert.Contains("not installed", availability.Reason, StringComparison.Ordinal);
        Assert.True(tesseract.ReportsConfidence);
        var error = await Assert.ThrowsAsync<AgendaImportException>(() => tesseract.RecognizeAsync(new byte[] { 1 }, new OcrRequest(), CancellationToken.None));
        Assert.Equal(AgendaErrorCodes.OcrUnavailable, error.Code);
    }

    [Fact]
    public void RotatingClockwiseMovesTheTopLeftToTheTopRight()
    {
        var pixels = Enumerable.Repeat((byte)255, 40 * 20).ToArray();
        pixels[0] = 0;
        var image = new GrayImage(40, 20, pixels);

        var rotated = image.Rotate(90, CancellationToken.None);

        Assert.Equal(20, rotated.Width);
        Assert.Equal(40, rotated.Height);
        Assert.True(rotated.Pixels[rotated.Width - 1] < 128);
        Assert.Equal(255, rotated.Pixels[0]);
    }

    [Fact]
    public void ScalingChangesTheSizeAndTransparentPixelsBecomePaper()
    {
        byte[] bgra = [0, 0, 0, 0, 0, 0, 0, 255];
        var image = GrayImage.FromPremultipliedBgra(bgra, 2, 1);

        Assert.Equal([255, 0], image.Pixels);
        var scaled = image.Scale(3, CancellationToken.None);
        Assert.Equal(6, scaled.Width);
        Assert.Equal(3, scaled.Height);
        Assert.Equal(255, scaled.Pixels[0]);
        Assert.Equal(0, scaled.Pixels[5]);
    }

    [Theory]
    [InlineData("png")]
    [InlineData("jpeg")]
    [InlineData("bmp")]
    [InlineData("tiff")]
    public void ReadsTheImageSizeFromTheHeader(string format)
    {
        using var bitmap = new Bitmap(123, 45);
        using var stream = new MemoryStream();
        bitmap.Save(stream, format switch
        {
            "png" => ImageFormat.Png,
            "jpeg" => ImageFormat.Jpeg,
            "bmp" => ImageFormat.Bmp,
            _ => ImageFormat.Tiff,
        });

        Assert.NotNull(ImageFormats.Detect(stream.ToArray()));
        Assert.True(ImageFormats.TryGetSize(stream.ToArray(), out var width, out var height));
        Assert.Equal(123, width);
        Assert.Equal(45, height);
    }

    [OcrFact]
    public async Task WindowsOcrIsAvailableWhenALanguageIsInstalled()
    {
        var engine = new WindowsOcrEngine();

        var availability = await engine.GetAvailabilityAsync(null, CancellationToken.None);
        var missing = await engine.GetAvailabilityAsync("qaa-x-none", CancellationToken.None);

        Assert.True(availability.IsAvailable);
        Assert.NotEmpty(availability.Languages);
        Assert.False(missing.IsAvailable);
        Assert.Contains("Windows Settings", missing.Reason, StringComparison.Ordinal);
    }

    [OcrFact]
    public async Task SmallTextIsUpscaledBeforeRecognition()
    {
        var page = await new WindowsOcrEngine().RecognizeAsync(Agendas.Fixture("ocr-small.png"), new OcrRequest(), CancellationToken.None);

        Assert.True(page.Scale > 1.5, $"Scale {page.Scale}");
        var heights = page.Words.Select(w => w.Height).Order().ToList();
        Assert.True(heights[heights.Count / 2] >= AgendaLimits.MinOcrWordHeight - 4, $"Median word height {heights[heights.Count / 2]}");
    }

    [OcrFact]
    public async Task ARotatedPhotoIsStraightened()
    {
        var page = await new WindowsOcrEngine().RecognizeAsync(Agendas.Fixture("ocr-photo.png"), new OcrRequest(), CancellationToken.None);

        Assert.NotNull(page.TextAngle);
        Assert.InRange(page.TextAngle!.Value, 2.0, 5.0);
        Assert.True(page.Deskewed);
    }
}
