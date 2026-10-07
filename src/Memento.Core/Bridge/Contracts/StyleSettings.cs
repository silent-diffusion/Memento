namespace Memento.Core.Bridge.Contracts;

/// <summary>The Style editor's settings (DESIGN.md §13, BRIDGE.md M4).</summary>
/// <param name="HeadingFace"><c>sans</c> or <c>serif</c>.</param>
/// <param name="BaseSize"><c>small</c>, <c>normal</c> or <c>large</c>.</param>
/// <param name="HeadingCase"><c>normal</c> or <c>smallCaps</c>.</param>
/// <param name="HeadingColor"><c>navy</c>, <c>ink</c>, <c>forest</c> or <c>burgundy</c>.</param>
/// <param name="Spacing"><c>tight</c>, <c>normal</c> or <c>airy</c>.</param>
/// <param name="Paper"><c>letter</c> or <c>a4</c>.</param>
public sealed record StyleSettings(
    string HeadingFace,
    string BodyFace,
    string BaseSize,
    string HeadingCase,
    bool NumberedHeadings,
    string HeadingColor,
    bool TableHeaderFill,
    bool RuleUnderTitle,
    bool LinesBetweenSections,
    string Spacing,
    string Paper,
    bool PageNumbers,
    bool RunningHeader);
