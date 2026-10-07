namespace Memento.Documents.Agenda.Text;

/// <summary>
/// One line of agenda text as a format parser read it, with whatever structure the format itself gave (a Word heading
/// style or numbering, a Markdown heading, a table's time column). <see cref="AgendaStructurer"/> turns lines into items.
/// </summary>
internal sealed record SourceLine(string Text, AgendaSourceLocation Location)
{
    /// <summary>Leading indentation in space-widths; only differences between lines matter.</summary>
    public int Indent { get; init; }

    /// <summary>An explicit heading level (1 = top) from the format; <c>null</c> lets the text decide.</summary>
    public int? HeadingLevel { get; init; }

    /// <summary>The format's own title (Word's Title style).</summary>
    public bool IsTitle { get; init; }

    /// <summary>A marker the format supplied (Word automatic numbering); the text is not searched for one.</summary>
    public ListMarker? Marker { get; init; }

    /// <summary>A time from another column of a table row.</summary>
    public string? Time { get; init; }

    /// <summary>Whether this line may be joined to the previous item as wrapped text. False for table cells.</summary>
    public bool MayContinue { get; init; } = true;

    /// <summary>The format saw a heading and the next line run together (a font change, a soft line break).</summary>
    public bool HeadingMergeSuspected { get; init; }

    /// <summary>Reasons the format parser already has for doubting this line (text recognition, ambiguous columns).</summary>
    public IReadOnlyList<string> Reasons { get; init; } = [];

    public bool IsBlank => string.IsNullOrWhiteSpace(Text);

    public static SourceLine Blank(AgendaSourceLocation location) => new(string.Empty, location);
}
