namespace Memento.Documents.Agenda.Text;

/// <summary>A list marker found at the start of a line, or given by the format (Word numbering).</summary>
/// <param name="Style">The kind of marker.</param>
/// <param name="Value">The ordinal (3 for "3.", "c)" or "iii."; the last part of an outline number); <c>null</c> for bullets.</param>
/// <param name="Label">The marker as written without punctuation ("3", "2.1", "c"), or <c>null</c> for bullets.</param>
/// <param name="Depth">The number of parts of an outline number ("2.1.3" is 3); 1 otherwise.</param>
/// <param name="BulletFamily">For bullets: 0 for - * • and similar, 1 for ◦ o – and similar, 2 for ▪ ■ ► and similar.</param>
/// <param name="AlternateRomanValue">For a single i, v or x: its value as a Roman numeral, which context decides between.</param>
internal sealed record ListMarker(
    MarkerStyle Style,
    int? Value,
    string? Label,
    int Depth = 1,
    int BulletFamily = 0,
    int? AlternateRomanValue = null)
{
    /// <summary>The outline parts before the last one ("2.1" for "2.1.3"), so sibling numbering is checked per parent.</summary>
    public string? OutlinePrefix => Style == MarkerStyle.Outline && Label is { } label && label.LastIndexOf('.') is var dot and > 0
        ? label[..dot]
        : null;

    public bool IsNumbered => Value is not null;
}
