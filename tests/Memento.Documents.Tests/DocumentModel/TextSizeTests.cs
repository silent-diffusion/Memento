using DocumentFormat.OpenXml.Wordprocessing;
using Memento.Documents.Model;
using Memento.Documents.Model.Blocks;
using Memento.Documents.Model.Modules;
using Memento.Documents.Render;
using Memento.Documents.Styling;
using Memento.Documents.Templates;
using Memento.Documents.Tests.DocumentModel.Support;
using Document = Memento.Documents.Model.Document;
using Run = Memento.Documents.Model.Run;

namespace Memento.Documents.Tests.DocumentModel;

/// <summary>A module's text size applies in the preview, the viewer, Word and PDF (PRODUCT-SPEC "Document Modules").</summary>
public sealed class TextSizeTests
{
    private static readonly DocumentHtmlRenderer Renderer = new();

    [Theory]
    [InlineData(TextSize.Smaller, 0.875)]
    [InlineData(TextSize.Normal, 1.0)]
    [InlineData(TextSize.Larger, 1.15)]
    public void FactorsMatchTheArchitecture(TextSize size, double factor) => Assert.Equal(factor, size.Factor());

    [Theory]
    [InlineData(TextSize.Smaller)]
    [InlineData(TextSize.Larger)]
    public void ViewerSkeletonAndPrintCarryTheModuleSize(TextSize size)
    {
        var doc = Single(size);
        var expected = $"class=\"paper-module size-{size.Name()}\"";
        Assert.Contains(expected, Renderer.RenderViewer(doc, BuiltInStyles.Corporate).Html, StringComparison.Ordinal);
        Assert.Contains(expected, Renderer.RenderPrintHtml(doc, BuiltInStyles.Corporate), StringComparison.Ordinal);
        var template = BuiltInTemplates.MeetingMinutes with
        {
            Rows = [new TemplateRow { Modules = [TemplateModule.FromCatalog(ModuleCatalog.Default.Get(ModuleIds.Notes), "n1") with { TextSize = size }] }],
        };
        Assert.Contains(expected, Renderer.RenderSkeleton(template, "T", new DocumentMeta(), BuiltInStyles.Corporate).Html, StringComparison.Ordinal);
        Assert.Contains(FormattableString.Invariant($".paper-module.size-{size.Name()}{{font-size:calc(var(--paper-base) * {size.Factor()})}}"), PaperCss.Stylesheet, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(TextSize.Smaller, "19")]
    [InlineData(TextSize.Larger, "25")]
    public void WordRunsAreScaledFromTheBaseSize(TextSize size, string halfPoints)
    {
        using var doc = DocxExportTests.Open(Single(size), BuiltInStyles.Corporate);
        var body = doc.MainDocumentPart!.Document!.Body!;
        var bodyRun = body.Descendants<DocumentFormat.OpenXml.Wordprocessing.Run>().First(r => r.InnerText == "Some module text.");
        Assert.Equal(halfPoints, bodyRun.RunProperties!.FontSize!.Val!.Value);
        var heading = body.Descendants<Paragraph>().First(p => p.ParagraphProperties?.ParagraphStyleId?.Val?.Value == "Heading1");
        Assert.Null(heading.Descendants<DocumentFormat.OpenXml.Wordprocessing.Run>().First().RunProperties);
    }

    [Fact]
    public void NormalTextUsesTheStyleSizeWithoutOverrides()
    {
        using var doc = DocxExportTests.Open(Single(TextSize.Normal), BuiltInStyles.Corporate);
        var bodyRun = doc.MainDocumentPart!.Document!.Body!.Descendants<DocumentFormat.OpenXml.Wordprocessing.Run>().First(r => r.InnerText == "Some module text.");
        Assert.Null(bodyRun.RunProperties);
    }

    private static Document Single(TextSize size) => new()
    {
        Id = "size",
        Title = "Sizes",
        Rows = [DocumentRow.Of(SampleDocuments.Module("x1", ModuleIds.Notes, "Notes", size, false, Provenance.FromUser(), new ParagraphBlock { Runs = [Run.Plain("Some module text.")] }))],
    };
}
