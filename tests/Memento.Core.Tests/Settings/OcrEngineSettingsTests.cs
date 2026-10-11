using System.Text.Json;
using Memento.Core.Bridge.Methods;
using Memento.Core.Models;
using Memento.Core.Settings;
using Memento.Core.Tests.Fakes;
using Memento.Core.Workers;
using Microsoft.Extensions.DependencyInjection;

namespace Memento.Core.Tests.Settings;

/// <summary>
/// 2.0: Settings › Documents › Agenda photos and scans (<c>documents.ocrEngine</c>): Windows OCR by default and the only
/// choice until Tesseract's language data is installed; the snapshot reports the engine in effect.
/// </summary>
public sealed class OcrEngineSettingsTests : IDisposable
{
    /// <summary>SHA-256 of four zero bytes, the "language data" the test installs.</summary>
    private const string Zero = "df3f619804a92fdb4057192dc43dd748ea778adc52bc498ce80524c014b81119";

    private static readonly ModelCatalog Catalog = ModelCatalog.Parse($$"""
        { "schemaVersion": 1, "models": [
          { "id": "tesseract-eng", "engine": "tesseract", "kind": "ocr", "name": "Tesseract English", "description": "d", "fileName": "eng.traineddata", "sizeBytes": 4, "sha256": "{{Zero}}", "url": "https://github.com/t", "license": "Apache-2.0", "runsOn": "cpu", "recommendedFor": "any", "accuracyNote": "Per-word confidence" }
        ] }
        """);

    private readonly BridgeTestHost _host = new(configure: services => services.AddSingleton(Catalog));

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task WindowsOcrIsTheDefault()
    {
        var settings = await _host.ResultAsync("settings.get");

        Assert.Equal("windows", settings.GetProperty("documents").GetProperty("ocrEngine").GetString());
        Assert.Null(_host.Settings.Current.Documents.OcrEngine);
    }

    [Fact]
    public async Task TesseractCannotBeChosenUntilItsLanguageDataIsInstalled()
    {
        var error = (await _host.CallAsync("settings.set", """{"documents":{"ocrEngine":"tesseract"}}""")).GetProperty("error");

        Assert.Equal("settings.invalidValue", error.GetProperty("code").GetString());
        Assert.Equal("documents.ocrEngine", error.GetProperty("detail").GetString());
        Assert.Contains("not installed", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Null(_host.Settings.Current.Documents.OcrEngine);
    }

    [Fact]
    public async Task OnceInstalledTesseractIsChosenAndNullGoesBackToWindowsOcr()
    {
        Install();

        var set = await _host.ResultAsync("settings.set", """{"documents":{"ocrEngine":"tesseract"}}""");
        Assert.Equal("tesseract", set.GetProperty("documents").GetProperty("ocrEngine").GetString());
        Assert.Equal(OcrEngines.Tesseract, _host.Settings.Current.Documents.OcrEngine);
        Assert.Equal("meeting-minutes", set.GetProperty("documents").GetProperty("defaultTemplateId").GetString());

        var cleared = await _host.ResultAsync("settings.set", """{"documents":{"ocrEngine":null}}""");
        Assert.Equal("windows", cleared.GetProperty("documents").GetProperty("ocrEngine").GetString());
        Assert.Null(_host.Settings.Current.Documents.OcrEngine);
    }

    [Fact]
    public async Task RemovingTheLanguageDataPutsWindowsOcrBackInEffect()
    {
        Install();
        await _host.ResultAsync("settings.set", """{"documents":{"ocrEngine":"tesseract"}}""");

        await _host.Models.RemoveAsync(OcrEngines.TesseractEnglishModelId, CancellationToken.None);
        var settings = await _host.ResultAsync("settings.get");

        Assert.Equal("windows", settings.GetProperty("documents").GetProperty("ocrEngine").GetString());
        Assert.Equal(OcrEngines.Tesseract, _host.Settings.Current.Documents.OcrEngine);
    }

    [Theory]
    [InlineData("""{"documents":{"ocrEngine":"abbyy"}}""")]
    [InlineData("""{"documents":{"ocrEngine":2}}""")]
    public async Task AnUnknownEngineIsRefused(string patch)
    {
        var error = (await _host.CallAsync("settings.set", patch)).GetProperty("error");

        Assert.Equal("documents.ocrEngine", error.GetProperty("detail").GetString());
    }

    [Theory]
    [InlineData(null, true, "windows")]
    [InlineData("tesseract", true, "tesseract")]
    [InlineData("tesseract", false, "windows")]
    [InlineData("windows", true, "windows")]
    public void TheEngineInEffectNeedsTheLanguageData(string? chosen, bool installed, string expected) =>
        Assert.Equal(expected, M4SettingsBlocks.EffectiveOcrEngine(chosen, installed));

    [Fact]
    public void TheOcrProtocolLinesAreCompactJson()
    {
        var job = new WorkerJob(WorkerJobKinds.Ocr, Ocr: new OcrJob(@"C:\m\tesseract", "eng"));
        Assert.Equal(
            """{"kind":"ocr","ocr":{"languageDataDirectory":"C:\\m\\tesseract","language":"eng"}}""",
            JsonSerializer.Serialize(job, WorkerJsonContext.Default.WorkerJob));
        Assert.False(job.UsesGpu);

        var image = OcrImage.Create(2, 2, 1, [0, 255]);
        Assert.Equal(
            """{"type":"recognize","image":{"id":2,"width":2,"height":1,"gray8":"AP8="}}""",
            JsonSerializer.Serialize(new WorkerCommand(WorkerMessageTypes.Recognize, Image: image), WorkerJsonContext.Default.WorkerCommand));

        var reply = JsonSerializer.Deserialize("""{"type":"recognized","ocr":{"id":2,"words":[{"text":"Budget","left":4,"top":5,"width":60,"height":20,"confidence":0.41}],"textAngle":3.59}}""", WorkerJsonContext.Default.WorkerReply)!;
        Assert.Equal(WorkerMessageTypes.Recognized, reply.Type);
        Assert.Equal(new OcrWord("Budget", 4, 5, 60, 20, 0.41), Assert.Single(reply.Ocr!.Words));
        Assert.Equal(3.59, reply.Ocr.TextAngle);
    }

    private void Install()
    {
        var entry = Catalog.Find(OcrEngines.TesseractEnglishModelId)!;
        var path = _host.Models.PathOf(entry);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[entry.SizeBytes]);
        Assert.Equal(ModelCheck.Verified, _host.Models.VerifyAsync(entry.Id, CancellationToken.None).GetAwaiter().GetResult());
    }
}
