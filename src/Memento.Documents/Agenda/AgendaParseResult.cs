namespace Memento.Documents.Agenda;

/// <summary>The parsed agenda, ready to show as an editable list.</summary>
/// <param name="Items">The items in document order.</param>
/// <param name="Source">What the content turned out to be (sniffed, not taken from the file name).</param>
/// <param name="SourceName">The file name as given, e.g. <c>agenda.docx</c>; <c>null</c> for pasted text.</param>
/// <param name="Title">A heading the parser took as the agenda's title ("Agenda", "Project kickoff agenda"), not as an item.</param>
/// <param name="Warnings">What the user should know; left-out content is carried in the warnings, never dropped.</param>
/// <param name="OcrEngine">The text recognition engine id (<c>windows</c>, <c>tesseract</c>) for images, else <c>null</c>.</param>
public sealed record AgendaParseResult(
    IReadOnlyList<ParsedAgendaItem> Items,
    AgendaSourceKind Source,
    string? SourceName,
    string? Title,
    IReadOnlyList<AgendaParseWarning> Warnings,
    string? OcrEngine = null)
{
    /// <summary>Always <c>true</c>: every parser runs on this PC and nothing is uploaded.</summary>
    public bool ParsedLocally { get; } = true;

    /// <summary>How many items are marked uncertain.</summary>
    public int UncertainCount => Items.Count(i => i.Uncertain);
}
