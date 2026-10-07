using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Agendas;

/// <summary>What <see cref="IAgendaReader"/> found, before the host adds the source name and the attachment token.</summary>
/// <param name="SourceKind">One of <see cref="AgendaSourceKinds"/>.</param>
/// <param name="OcrEngine">"Windows OCR" or "Tesseract" when text recognition read it; otherwise <c>null</c>.</param>
public sealed record AgendaReading(
    string SourceKind,
    string? Title,
    IReadOnlyList<AgendaParsedItem> Items,
    IReadOnlyList<AgendaWarning> Warnings,
    string? OcrEngine);
