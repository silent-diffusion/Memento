namespace Memento.Core.Bridge.Contracts;

/// <summary>A parsed agenda shown for review before <c>agenda.apply</c>.</summary>
/// <param name="Source">The file name, or "Pasted text".</param>
/// <param name="SourceKind"><c>text</c>, <c>pastedText</c>, <c>markdown</c>, <c>csv</c>, <c>tsv</c>, <c>docx</c>, <c>xlsx</c>, <c>pdf</c> or <c>image</c>.</param>
/// <param name="OcrEngine">"Windows OCR" (or "Tesseract") when the agenda was read from an image or a scanned PDF.</param>
/// <param name="AttachmentToken">The host keeps the original file under this handle until <c>agenda.apply</c> or <c>agenda.discard</c>.</param>
public sealed record AgendaParsePreview(
    string Source,
    string SourceKind,
    string? Title,
    IReadOnlyList<AgendaParsedItem> Items,
    IReadOnlyList<AgendaWarning> Warnings,
    string? OcrEngine,
    string? AttachmentToken);
