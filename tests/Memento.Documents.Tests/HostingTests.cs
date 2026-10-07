using Memento.Core.Agendas;
using Memento.Core.Bridge;
using Memento.Documents.Agenda;
using Memento.Documents.Agenda.Hosting;
using Memento.Documents.Agenda.Ocr;
using Memento.Documents.Agenda.Pdf;
using Memento.Documents.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using UglyToad.PdfPig.Writer;

namespace Memento.Documents.Tests;

/// <summary>The parsers behind Core's <see cref="IAgendaReader"/> (M3): mapping, error codes, scanned PDFs.</summary>
public sealed class HostingTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "memento-documents-" + Guid.NewGuid().ToString("N"));

    public HostingTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public async Task AWordFixtureReadsAsADocxPreview()
    {
        var path = Path.Combine(_folder, "messy-mixed.docx");
        File.WriteAllBytes(path, Agendas.Fixture("messy-mixed.docx"));
        var reader = new DocumentsAgendaReader(Agendas.Importer());

        var reading = await reader.ReadFileAsync(path, CancellationToken.None);

        Assert.Equal(AgendaSourceKinds.Docx, reading.SourceKind);
        Assert.Equal("Neighbourhood Garden Association", reading.Title);
        Assert.Equal("Welcome from the chair", reading.Items[0].Text);
        Assert.Contains(reading.Items, i => i.Level == 1 && i.Text == "Chair");
        Assert.Contains(reading.Items, i => i.Uncertain && i.UncertainReason is { Length: > 0 });
        Assert.All(reading.Items, i => Assert.NotNull(i.Location));
        Assert.Contains(reading.Warnings, w => w.Code == AgendaWarningCodes.TableSkipped && w.Message.Length > 0);
        Assert.Null(reading.OcrEngine);
    }

    [Fact]
    public async Task PastedTextReadsAsPastedText()
    {
        var reading = await new DocumentsAgendaReader(Agendas.Importer()).ReadTextAsync("1. Welcome\n2. Budget\n3. Close", CancellationToken.None);

        Assert.Equal(AgendaSourceKinds.PastedText, reading.SourceKind);
        Assert.Equal(["Welcome", "Budget", "Close"], reading.Items.Select(i => i.Text));
        Assert.All(reading.Items, i => Assert.Equal(0, i.Level));
    }

    [Theory]
    [InlineData(AgendaSourceKind.Text, "text")]
    [InlineData(AgendaSourceKind.PastedText, "pastedText")]
    [InlineData(AgendaSourceKind.Markdown, "markdown")]
    [InlineData(AgendaSourceKind.Csv, "csv")]
    [InlineData(AgendaSourceKind.Tsv, "tsv")]
    [InlineData(AgendaSourceKind.Docx, "docx")]
    [InlineData(AgendaSourceKind.Xlsx, "xlsx")]
    [InlineData(AgendaSourceKind.Pdf, "pdf")]
    [InlineData(AgendaSourceKind.Image, "image")]
    public void EverySourceKindHasItsBridgeName(AgendaSourceKind kind, string name)
    {
        Assert.Equal(name, DocumentsAgendaReader.Kind(kind));
        Assert.True(AgendaSourceKinds.IsValid(name));
    }

    [Fact]
    public async Task ImportErrorsKeepTheirAgendaCodeAndMessage()
    {
        var path = Path.Combine(_folder, "empty.txt");
        File.WriteAllBytes(path, []);

        var error = await Assert.ThrowsAsync<BridgeException>(() => new DocumentsAgendaReader(Agendas.Importer()).ReadFileAsync(path, CancellationToken.None));

        Assert.Equal(DomainErrorCodes.AgendaNoText, error.Code);
        Assert.Contains("empty.txt", error.Message, StringComparison.Ordinal);
        Assert.Contains("Nothing was imported", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryAgendaErrorCodeIsABridgeCode()
    {
        var library = typeof(AgendaErrorCodes).GetFields().Select(f => (string)f.GetRawConstantValue()!).ToList();
        var bridge = typeof(DomainErrorCodes).GetFields().Select(f => (string)f.GetRawConstantValue()!).ToList();

        Assert.All(library, code => Assert.Contains(code, bridge));
    }

    [Fact]
    public async Task OcrUnavailableSaysHowToInstallALanguage()
    {
        var path = Path.Combine(_folder, "photo.png");
        File.WriteAllBytes(path, Agendas.Fixture("ocr-small.png"));

        var error = await Assert.ThrowsAsync<BridgeException>(() => new DocumentsAgendaReader(Agendas.Importer()).ReadFileAsync(path, CancellationToken.None));

        Assert.Equal(DomainErrorCodes.AgendaOcrUnavailable, error.Code);
        Assert.Equal(DocumentsAgendaReader.OcrLanguageHelp, error.Detail);
        Assert.Contains("Windows Settings", error.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnImageNamesTheOcrEngine()
    {
        var path = Path.Combine(_folder, "photo.png");
        File.WriteAllBytes(path, Agendas.Fixture("ocr-small.png"));
        var engine = new FakeOcrEngine("windows", available: true, FakeOcrEngine.Lines("Agenda", "1. Welcome", "2. Close"));

        var reading = await new DocumentsAgendaReader(Agendas.Importer(engine)).ReadFileAsync(path, CancellationToken.None);

        Assert.Equal(AgendaSourceKinds.Image, reading.SourceKind);
        Assert.Equal("Windows OCR", reading.OcrEngine);
    }

    [Fact]
    public async Task AScannedPdfIsRenderedAndReadWithTextRecognition()
    {
        var engine = new FakeOcrEngine("windows", available: true, FakeOcrEngine.Lines("Board meeting agenda", "1. Welcome", "2. Budget review", "3. Any other business"));
        var renderer = new FakeRenderer(pages: 2);
        var importer = new AgendaImporter(AgendaImporter.CreateParsers([engine], renderer));

        var result = await Agendas.ImportAsync(BlankPdf(2), "scan.pdf", importer);

        Assert.Equal(AgendaSourceKind.Pdf, result.Source);
        Assert.Equal("windows", result.OcrEngine);
        Assert.Equal(ScannedPdfReader.Dpi, renderer.Dpi);
        Assert.Equal(2, engine.Calls);
        Assert.Equal(AgendaWarningCodes.ScannedPdf, result.Warnings[0].Code);
        Assert.Contains("Welcome", result.Items.Select(i => i.Text));
        Assert.Contains(result.Items, i => i.Location.Page == 2);
    }

    [Fact]
    public async Task AScanWithoutWordsIsStillNoText()
    {
        var importer = new AgendaImporter(AgendaImporter.CreateParsers([new FakeOcrEngine("windows", available: true)], new FakeRenderer(1)));

        var error = await Assert.ThrowsAsync<AgendaImportException>(() => Agendas.ImportAsync(BlankPdf(1), "scan.pdf", importer));

        Assert.Equal(AgendaErrorCodes.NoText, error.Code);
        Assert.Contains("scan", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WindowsRendersPdfPagesAsPngAtTheRequestedResolution()
    {
        var pages = await new WindowsPdfPageRenderer().RenderAsync(BlankPdf(2), 200, 10, AgendaParseOptions.Default, CancellationToken.None);

        Assert.Equal(2, pages.Count);
        Assert.All(pages, png => Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png[..4]));

        // A4 is 595 × 842 pt: 1653 × 2339 px at 200 dpi. The PNG header holds the size big-endian at bytes 16–23.
        var width = (pages[0][16] << 24) | (pages[0][17] << 16) | (pages[0][18] << 8) | pages[0][19];
        Assert.InRange(width, 1640, 1665);
    }

    [OcrFact]
    public async Task AScannedPdfIsReadByWindowsOcr()
    {
        // The photo fixture embedded as the only content of a page: a PDF with no text layer.
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(595, 842);
        page.AddJpeg(Agendas.Fixture("ocr-photo.jpg"), new UglyToad.PdfPig.Core.PdfRectangle(40, 300, 555, 800));
        var importer = AgendaImporter.CreateDefault();

        var result = await Agendas.ImportAsync(builder.Build(), "scanned-agenda.pdf", importer);

        Assert.Equal(AgendaSourceKind.Pdf, result.Source);
        Assert.Equal("windows", result.OcrEngine);
        Assert.Equal(AgendaWarningCodes.ScannedPdf, result.Warnings[0].Code);
        Assert.Contains(result.Items, i => i.Text.Contains("Seed swap", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AddAgendaReaderReplacesTheUnavailableReader()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAgendaReader, UnavailableAgendaReader>();
        services.AddAgendaReader();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<DocumentsAgendaReader>(provider.GetRequiredService<IAgendaReader>());
    }

    private static byte[] BlankPdf(int pages)
    {
        var builder = new PdfDocumentBuilder();
        for (var i = 0; i < pages; i++)
        {
            builder.AddPage(595, 842);
        }

        return builder.Build();
    }

    /// <summary>Stands in for Windows' renderer: one tiny PNG per page.</summary>
    private sealed class FakeRenderer(int pages) : IPdfPageRenderer
    {
        public int Dpi { get; private set; }

        public Task<IReadOnlyList<byte[]>> RenderAsync(ReadOnlyMemory<byte> pdf, int dpi, int maxPages, AgendaParseOptions options, CancellationToken cancellationToken)
        {
            Dpi = dpi;
            IReadOnlyList<byte[]> images = Enumerable.Range(0, Math.Min(pages, maxPages)).Select(_ => Agendas.Fixture("ocr-small.png")).ToArray();
            return Task.FromResult(images);
        }
    }
}
