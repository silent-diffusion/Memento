using System.Text.Json.Nodes;
using Memento.Documents.Model.Storage;
using Memento.Documents.Styling;
using Memento.Documents.Tests.DocumentModel.Support;

namespace Memento.Documents.Tests.DocumentModel;

public sealed class StyleTests
{
    [Fact]
    public void CorporatePresetHasTheDocumentedValues()
    {
        var s = BuiltInStyles.Corporate;
        Assert.Equal((Typeface.Sans, Typeface.Sans, BaseSize.Normal, HeadingCase.SmallCaps, false), (s.HeadingTypeface, s.BodyTypeface, s.BaseSize, s.HeadingCase, s.NumberedHeadings));
        Assert.Equal((HeadingColor.Navy, true, true, false, Spacing.Normal), (s.HeadingColor, s.TableHeaderFill, s.RuleUnderTitle, s.LinesBetweenSections, s.Spacing));
    }

    [Fact]
    public void MinimalPresetHasTheDocumentedValues()
    {
        var s = BuiltInStyles.Minimal;
        Assert.Equal((Typeface.Sans, Typeface.Sans, BaseSize.Normal, HeadingCase.Normal, false), (s.HeadingTypeface, s.BodyTypeface, s.BaseSize, s.HeadingCase, s.NumberedHeadings));
        Assert.Equal((HeadingColor.Ink, false, false, true, Spacing.Airy), (s.HeadingColor, s.TableHeaderFill, s.RuleUnderTitle, s.LinesBetweenSections, s.Spacing));
    }

    [Fact]
    public void AcademicPresetHasTheDocumentedValues()
    {
        var s = BuiltInStyles.Academic;
        Assert.Equal((Typeface.Serif, Typeface.Serif, BaseSize.Normal, HeadingCase.Normal, true), (s.HeadingTypeface, s.BodyTypeface, s.BaseSize, s.HeadingCase, s.NumberedHeadings));
        Assert.Equal((HeadingColor.Ink, false, false, false, Spacing.Normal), (s.HeadingColor, s.TableHeaderFill, s.RuleUnderTitle, s.LinesBetweenSections, s.Spacing));
    }

    [Fact]
    public void PresetsArePresetsOfTheSameSettings()
    {
        Assert.Equal(["Corporate", "Minimal", "Academic"], BuiltInStyles.All.Select(s => s.Name));
        Assert.All(BuiltInStyles.All, s =>
        {
            Assert.True(s.BuiltIn);
            Assert.Equal(PaperSize.Letter, s.Paper);
            Assert.True(s.PageNumbers);
            Assert.Null(s.ExtensionData);
        });
    }

    [Theory]
    [InlineData(BaseSize.Small, 11, 10)]
    [InlineData(BaseSize.Normal, 12, 11)]
    [InlineData(BaseSize.Large, 13.5, 12.5)]
    public void BaseSizesMatchTheStyleEditor(BaseSize size, double px, double pt)
    {
        var style = BuiltInStyles.Corporate with { BaseSize = size };
        Assert.Equal(px, style.ScreenBasePx());
        Assert.Equal(pt, style.PrintBasePt());
    }

    [Theory]
    [InlineData(Spacing.Tight, 10)]
    [InlineData(Spacing.Normal, 18)]
    [InlineData(Spacing.Airy, 28)]
    public void SpacingMatchesTheStyleEditor(Spacing spacing, double px) =>
        Assert.Equal(px, (BuiltInStyles.Minimal with { Spacing = spacing }).SpacingPx());

    [Theory]
    [InlineData(HeadingColor.Navy, "#1F3A5F", "#D9E1EC")]
    [InlineData(HeadingColor.Ink, "#1D1C1A", "#E8E6E0")]
    [InlineData(HeadingColor.Forest, "#2F6B4F", "#DCE9E1")]
    [InlineData(HeadingColor.Burgundy, "#7A2E2E", "#EEDCDC")]
    public void HeadingColoursHavePairedTints(HeadingColor color, string hex, string tint)
    {
        Assert.Equal(hex, StyleMetrics.HeadingHex(color));
        Assert.Equal(tint, StyleMetrics.TintHex(color));
    }

    [Fact]
    public void PaperSizesAreLetterAndA4()
    {
        Assert.Equal((12240u, 15840u), StyleMetrics.PaperTwips(PaperSize.Letter));
        Assert.Equal((11906u, 16838u), StyleMetrics.PaperTwips(PaperSize.A4));
        Assert.Equal("letter", StyleMetrics.CssPageSize(PaperSize.Letter));
        Assert.Equal("A4", StyleMetrics.CssPageSize(PaperSize.A4));
    }

    [Fact]
    public void StyleJsonUsesCamelCaseEnumNames()
    {
        var node = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(BuiltInStyles.Corporate with { Paper = PaperSize.A4 }, StyleJsonContext.Default.DocumentStyle))!;
        Assert.Equal("smallCaps", (string)node["headingCase"]!);
        Assert.Equal("navy", (string)node["headingColor"]!);
        Assert.Equal("a4", (string)node["paper"]!);
        Assert.Null(node["customized"]);
    }

    [Fact]
    public async Task StoreSavesDuplicatesResetsAndDeletes()
    {
        using var folder = new TempFolder();
        var store = new FileStyleStore(folder.Path);
        var copy = await store.DuplicateAsync(BuiltInStyles.CorporateId, "House style", CancellationToken.None);
        Assert.Equal("house-style", copy.Id);
        Assert.False(copy.BuiltIn);
        Assert.Equal(HeadingColor.Navy, copy.HeadingColor);

        var changed = await store.SaveAsync(copy with { HeadingColor = HeadingColor.Forest }, CancellationToken.None);
        Assert.Equal(HeadingColor.Forest, (await store.GetAsync("house-style", CancellationToken.None))!.HeadingColor);
        Assert.Equal(["corporate", "minimal", "academic", "house-style"], (await store.ListAsync(CancellationToken.None)).Select(s => s.Id));
        Assert.False(File.Exists(folder.File("house-style.json.tmp")));

        await store.SaveAsync(BuiltInStyles.Academic with { Paper = PaperSize.A4 }, CancellationToken.None);
        Assert.True((await store.GetAsync("academic", CancellationToken.None))!.Customized);
        var reset = await store.ResetBuiltInAsync("academic", CancellationToken.None);
        Assert.Equal(PaperSize.Letter, reset.Paper);

        await store.DeleteAsync(changed.Id, CancellationToken.None);
        Assert.Null(await store.GetAsync(changed.Id, CancellationToken.None));
        var error = await Assert.ThrowsAsync<DocumentStoreException>(() => store.DeleteAsync("minimal", CancellationToken.None));
        Assert.Equal(StoreErrorCodes.BuiltInCannotBeDeleted, error.Code);
        Assert.Contains("Reset", error.Message, StringComparison.Ordinal);
    }
}
