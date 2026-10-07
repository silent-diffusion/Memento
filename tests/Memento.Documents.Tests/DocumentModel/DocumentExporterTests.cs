using System.Security.Cryptography;
using System.Text;
using Memento.Documents.Export;
using Memento.Documents.Model.Modules;
using Memento.Documents.Render;
using Memento.Documents.Styling;
using Memento.Documents.Templates;
using Memento.Documents.Tests.DocumentModel.Support;
using Microsoft.Extensions.DependencyInjection;

namespace Memento.Documents.Tests.DocumentModel;

public sealed class DocumentExporterTests
{
    [Theory]
    [InlineData(DocumentExportFormat.Docx, ".docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData(DocumentExportFormat.Markdown, ".md", "text/markdown; charset=utf-8")]
    [InlineData(DocumentExportFormat.Pdf, ".pdf", "application/pdf")]
    public async Task EachFormatReturnsBytesWithTheirSha256(DocumentExportFormat format, string extension, string mediaType)
    {
        var exporter = new DocumentExporter(new FakePdfPrinter());
        var result = await exporter.ExportAsync(SampleDocuments.MeetingMinutes(), BuiltInStyles.Corporate, format, CancellationToken.None);
        Assert.Equal(format, result.Format);
        Assert.Equal(extension, result.FileExtension);
        Assert.Equal(mediaType, result.MediaType);
        Assert.True(result.Length > 0);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(result.Content.Span)).ToLowerInvariant(), result.Sha256);
        Assert.Equal(64, result.Sha256.Length);
    }

    [Fact]
    public async Task MarkdownIsUtf8WithoutABom()
    {
        var result = await new DocumentExporter().ExportAsync(SampleDocuments.MeetingMinutes(), BuiltInStyles.Corporate, DocumentExportFormat.Markdown, CancellationToken.None);
        Assert.NotEqual(0xEF, result.Content.Span[0]);
        Assert.Equal(new MarkdownExporter().Export(SampleDocuments.MeetingMinutes()), Encoding.UTF8.GetString(result.Content.Span));
    }

    [Fact]
    public async Task PdfPrintsThePrintHtmlWithThePageSettings()
    {
        var printer = new FakePdfPrinter();
        var style = BuiltInStyles.Academic with { Paper = PaperSize.A4 };
        await new DocumentExporter(printer).ExportAsync(SampleDocuments.MeetingMinutes(), style, DocumentExportFormat.Pdf, CancellationToken.None);
        Assert.Equal(new DocumentHtmlRenderer().RenderPrintHtml(SampleDocuments.MeetingMinutes(), style), printer.Html);
        Assert.Equal(new PdfPrintOptions(210 / 25.4, 297 / 25.4, 0.9, 0.9, 1, 1), printer.Options);
    }

    [Fact]
    public async Task PdfWithoutAPrinterOrWithAFailingOneIsSpecific()
    {
        var missing = await Assert.ThrowsAsync<DocumentExportException>(() =>
            new DocumentExporter().ExportAsync(SampleDocuments.MeetingMinutes(), BuiltInStyles.Corporate, DocumentExportFormat.Pdf, CancellationToken.None));
        Assert.Equal(DocumentExportErrorCodes.PdfPrinterUnavailable, missing.Code);
        Assert.Contains("Design review: library screen", missing.Message, StringComparison.Ordinal);

        var failing = new FakePdfPrinter { Failure = new InvalidOperationException("WebView2 is not ready") };
        var failed = await Assert.ThrowsAsync<DocumentExportException>(() =>
            new DocumentExporter(failing).ExportAsync(SampleDocuments.MeetingMinutes(), BuiltInStyles.Corporate, DocumentExportFormat.Pdf, CancellationToken.None));
        Assert.Equal(DocumentExportErrorCodes.PdfFailed, failed.Code);
        Assert.Contains("WebView2 is not ready", failed.Message, StringComparison.Ordinal);
        Assert.Contains("unchanged", failed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportToFileWritesAtomically()
    {
        using var folder = new TempFolder();
        var path = folder.File("minutes.docx");
        var result = await new DocumentExporter().ExportToFileAsync(SampleDocuments.MeetingMinutes(), BuiltInStyles.Corporate, DocumentExportFormat.Docx, path, CancellationToken.None);
        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
        Assert.Equal(result.Sha256, DocumentExporter.Sha256Hex(File.ReadAllBytes(path)));
    }

    [Fact]
    public async Task CancellationIsHonoured()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new DocumentExporter(new FakePdfPrinter()).ExportAsync(SampleDocuments.MeetingMinutes(), BuiltInStyles.Corporate, DocumentExportFormat.Docx, cancelled.Token));
    }

    [Fact]
    public async Task AddDocumentModelRegistersTheServices()
    {
        using var folder = new TempFolder();
        var services = new ServiceCollection()
            .AddSingleton<IPdfPrinter>(new FakePdfPrinter())
            .AddDocumentModel(Path.Combine(folder.Path, "templates"), Path.Combine(folder.Path, "styles"));
        await using var provider = services.BuildServiceProvider();
        Assert.Same(ModuleCatalog.Default, provider.GetRequiredService<ModuleCatalog>());
        Assert.Equal(4, (await provider.GetRequiredService<ITemplateStore>().ListAsync(CancellationToken.None)).Count);
        Assert.Equal(3, (await provider.GetRequiredService<IStyleStore>().ListAsync(CancellationToken.None)).Count);
        var pdf = await provider.GetRequiredService<DocumentExporter>().ExportAsync(SampleDocuments.AllShapes(), BuiltInStyles.Minimal, DocumentExportFormat.Pdf, CancellationToken.None);
        Assert.True(pdf.Length > 0);
    }
}
