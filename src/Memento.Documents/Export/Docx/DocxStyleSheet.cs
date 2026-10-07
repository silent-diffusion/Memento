using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;
using Memento.Documents.Styling;

namespace Memento.Documents.Export.Docx;

/// <summary>Maps a <see cref="DocumentStyle"/> to Word paragraph and character styles: fonts, sizes, heading colour and case, numbering, rules.</summary>
internal static class DocxStyleSheet
{
    /// <summary>The numbering instance that numbers module headings (when the style numbers them).</summary>
    public const int HeadingNumberingId = 2;

    public const int BulletNumberingId = 1;

    public static Styles Build(DocumentStyle style)
    {
        var basePt = style.PrintBasePt();
        var head = DocxUnits.Hex(style.HeadingHex());
        var headFont = StyleMetrics.WordFont(style.HeadingTypeface);
        var bodyFont = StyleMetrics.WordFont(style.BodyTypeface);
        var gap = style.SpacingPt();
        var caps = style.HeadingCase == HeadingCase.SmallCaps;
        var headingSize = caps ? basePt * 0.8 : basePt * 1.05;

        var styles = new Styles(
            new DocDefaults(
                new RunPropertiesDefault(new RunPropertiesBaseStyle(
                    new RunFonts { Ascii = bodyFont, HighAnsi = bodyFont, ComplexScript = bodyFont, EastAsia = bodyFont },
                    new Color { Val = DocxUnits.Hex(StyleMetrics.Ink) },
                    new FontSize { Val = DocxUnits.HalfPoints(basePt) },
                    new FontSizeComplexScript { Val = DocxUnits.HalfPoints(basePt) },
                    new Languages { Val = "en-US" })),
                new ParagraphPropertiesDefault(new ParagraphPropertiesBaseStyle(
                    new SpacingBetweenLines { Before = "0", After = "0", Line = "276", LineRule = LineSpacingRuleValues.Auto }))));

        styles.Append(Paragraph(DocxStyleIds.Normal, "Normal", null, isDefault: true, primary: true,
            [new SpacingBetweenLines { After = DocxUnits.TwipsText(basePt * 0.5) }], []));

        var titlePpr = new List<OpenXmlElement> { new KeepNext() };
        if (style.RuleUnderTitle)
        {
            titlePpr.Add(new ParagraphBorders(new BottomBorder { Val = BorderValues.Single, Size = 12, Space = 6, Color = head }));
        }

        titlePpr.Add(new SpacingBetweenLines { After = DocxUnits.TwipsText(basePt * 0.4), Line = "240", LineRule = LineSpacingRuleValues.Auto });
        styles.Append(Paragraph(DocxStyleIds.Title, "Title", DocxStyleIds.Normal, isDefault: false, primary: true, titlePpr,
            new RunFormat { Font = headFont, Bold = true, Color = head, Spacing = DocxUnits.Twips(basePt * 2 * -0.02), Size = basePt * 2 }.Elements()));

        styles.Append(Paragraph(DocxStyleIds.Subtitle, "Subtitle", DocxStyleIds.Normal, isDefault: false, primary: true,
            [new SpacingBetweenLines { After = "0" }],
            new RunFormat { Color = DocxUnits.Hex(StyleMetrics.MetaInk), Size = basePt * 0.85 }.Elements()));

        var h1Ppr = new List<OpenXmlElement> { new KeepNext() };
        if (style.NumberedHeadings)
        {
            h1Ppr.Add(new NumberingProperties(new NumberingLevelReference { Val = 0 }, new NumberingId { Val = HeadingNumberingId }));
        }

        h1Ppr.Add(new SpacingBetweenLines { Before = DocxUnits.TwipsText(gap), After = DocxUnits.TwipsText(basePt * 0.45), Line = "240", LineRule = LineSpacingRuleValues.Auto });
        h1Ppr.Add(new OutlineLevel { Val = 0 });
        styles.Append(Paragraph(DocxStyleIds.Heading1, "heading 1", DocxStyleIds.Normal, isDefault: false, primary: true, h1Ppr,
            new RunFormat
            {
                Font = headFont,
                Bold = true,
                Caps = caps,
                Color = head,
                Spacing = caps ? DocxUnits.Twips(headingSize * 0.1) : null,
                Size = headingSize,
            }.Elements()));

        var subSizes = new[] { 1.05, 1.0, 0.95 };
        var subIds = new[] { DocxStyleIds.Heading2, DocxStyleIds.Heading3, DocxStyleIds.Heading4 };
        for (var level = 0; level < subIds.Length; level++)
        {
            styles.Append(Paragraph(subIds[level], $"heading {level + 2}", DocxStyleIds.Normal, isDefault: false, primary: true,
                [
                    new KeepNext(),
                    new SpacingBetweenLines { Before = DocxUnits.TwipsText(basePt * 0.6), After = DocxUnits.TwipsText(basePt * 0.3) },
                    new OutlineLevel { Val = level + 1 },
                ],
                new RunFormat { Font = headFont, Bold = true, Color = head, Size = basePt * subSizes[level] }.Elements()));
        }

        styles.Append(Paragraph(DocxStyleIds.ListParagraph, "List Paragraph", DocxStyleIds.Normal, isDefault: false, primary: true,
            [new SpacingBetweenLines { After = DocxUnits.TwipsText(basePt * 0.3) }], []));

        styles.Append(Paragraph(DocxStyleIds.Quote, "Quote", DocxStyleIds.Normal, isDefault: false, primary: true,
            [
                new ParagraphBorders(new LeftBorder { Val = BorderValues.Single, Size = 12, Space = 8, Color = DocxUnits.Hex(StyleMetrics.QuoteBar) }),
                new SpacingBetweenLines { After = DocxUnits.TwipsText(basePt * 0.3) },
                new Indentation { Left = "200" },
            ], []));

        styles.Append(Paragraph(DocxStyleIds.TableText, "Table Text", DocxStyleIds.Normal, isDefault: false, primary: false,
            [new SpacingBetweenLines { Before = "0", After = "0" }], []));

        styles.Append(Paragraph(DocxStyleIds.FootnoteText, "footnote text", DocxStyleIds.Normal, isDefault: false, primary: false,
            [new SpacingBetweenLines { After = "0" }],
            new RunFormat { Color = DocxUnits.Hex(StyleMetrics.ChipInk), Size = basePt * 0.8 }.Elements()));

        var contentWidth = ContentWidthTwips(style);
        styles.Append(Paragraph(DocxStyleIds.Header, "header", DocxStyleIds.Normal, isDefault: false, primary: false,
            [
                new ParagraphBorders(new BottomBorder { Val = BorderValues.Single, Size = 4, Space = 4, Color = DocxUnits.Hex(StyleMetrics.Hairline) }),
                new Tabs(new TabStop { Val = TabStopValues.Right, Position = contentWidth }),
                new SpacingBetweenLines { After = "0" },
            ],
            new RunFormat { Color = DocxUnits.Hex(StyleMetrics.FaintInk), Size = basePt * 0.78 }.Elements()));

        styles.Append(Paragraph(DocxStyleIds.Footer, "footer", DocxStyleIds.Normal, isDefault: false, primary: false,
            [new SpacingBetweenLines { After = "0" }, new Justification { Val = JustificationValues.Center }],
            new RunFormat { Color = DocxUnits.Hex(StyleMetrics.FaintInk), Size = basePt * 0.78 }.Elements()));

        var footnoteReference = new Style(
            new StyleName { Val = "footnote reference" },
            new UIPriority { Val = 99 },
            new StyleRunProperties(new VerticalTextAlignment { Val = VerticalPositionValues.Superscript }))
        {
            Type = StyleValues.Character,
            StyleId = DocxStyleIds.FootnoteReference,
        };
        styles.Append(footnoteReference);
        return styles;
    }

    /// <summary>Width between the side margins in twips.</summary>
    public static int ContentWidthTwips(DocumentStyle style)
    {
        var (width, _) = StyleMetrics.PaperTwips(style.Paper);
        return (int)width - (2 * DocxUnits.InchesToTwips(StyleMetrics.MarginSideInches));
    }

    /// <summary>Abstract numbering: 1 bullets, 2 decimal lists, 3 module headings "1.".</summary>
    public static IEnumerable<AbstractNum> AbstractNumbers()
    {
        string[] bullets = ["•", "◦", "▪"];
        var bullet = new AbstractNum(new MultiLevelType { Val = MultiLevelValues.HybridMultilevel }) { AbstractNumberId = 1 };
        for (var level = 0; level < 3; level++)
        {
            bullet.Append(Level(level, NumberFormatValues.Bullet, bullets[level], 360 * (level + 1), 270));
        }

        yield return bullet;

        var decimals = new AbstractNum(new MultiLevelType { Val = MultiLevelValues.HybridMultilevel }) { AbstractNumberId = 2 };
        decimals.Append(Level(0, NumberFormatValues.Decimal, "%1.", 360, 360));
        decimals.Append(Level(1, NumberFormatValues.LowerLetter, "%2.", 720, 360));
        decimals.Append(Level(2, NumberFormatValues.LowerRoman, "%3.", 1080, 360));
        yield return decimals;

        var headings = new AbstractNum(new MultiLevelType { Val = MultiLevelValues.SingleLevel }) { AbstractNumberId = 3 };
        headings.Append(new Level(
            new StartNumberingValue { Val = 1 },
            new NumberingFormat { Val = NumberFormatValues.Decimal },
            new ParagraphStyleIdInLevel { Val = DocxStyleIds.Heading1 },
            new LevelSuffix { Val = LevelSuffixValues.Space },
            new LevelText { Val = "%1." },
            new LevelJustification { Val = LevelJustificationValues.Left },
            new PreviousParagraphProperties(new Indentation { Left = "0", Hanging = "0" }))
        { LevelIndex = 0 });
        yield return headings;
    }

    private static Level Level(int index, NumberFormatValues format, string text, int left, int hanging)
    {
        var level = new Level(
            new StartNumberingValue { Val = 1 },
            new NumberingFormat { Val = format },
            new LevelText { Val = text },
            new LevelJustification { Val = LevelJustificationValues.Left },
            new PreviousParagraphProperties(new Indentation { Left = left.ToString(System.Globalization.CultureInfo.InvariantCulture), Hanging = hanging.ToString(System.Globalization.CultureInfo.InvariantCulture) }))
        { LevelIndex = index };
        return level;
    }

    private static Style Paragraph(string id, string name, string? basedOn, bool isDefault, bool primary, IEnumerable<OpenXmlElement> paragraph, IEnumerable<OpenXmlElement> run)
    {
        var style = new Style { Type = StyleValues.Paragraph, StyleId = id };
        if (isDefault)
        {
            style.Default = true;
        }

        style.Append(new StyleName { Val = name });
        if (basedOn is not null)
        {
            style.Append(new BasedOn { Val = basedOn });
            style.Append(new NextParagraphStyle { Val = DocxStyleIds.Normal });
        }

        if (primary)
        {
            style.Append(new PrimaryStyle());
        }

        var ppr = paragraph.ToList();
        if (ppr.Count > 0)
        {
            style.Append(new StyleParagraphProperties(ppr));
        }

        var rpr = run.ToList();
        if (rpr.Count > 0)
        {
            style.Append(new StyleRunProperties(rpr));
        }

        return style;
    }
}
