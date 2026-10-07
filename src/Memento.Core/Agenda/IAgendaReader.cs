namespace Memento.Core.Agendas;

/// <summary>
/// Reads an agenda on this PC (Memento.Documents implements it with its parsers and OCR). Failures are
/// <see cref="Bridge.BridgeException"/>s with the <c>agenda.*</c> codes and DESIGN.md §17 messages.
/// </summary>
public interface IAgendaReader
{
    /// <summary>Reads a file; its size is checked before anything is parsed.</summary>
    Task<AgendaReading> ReadFileAsync(string path, CancellationToken cancellationToken);

    /// <summary>Reads pasted text.</summary>
    Task<AgendaReading> ReadTextAsync(string text, CancellationToken cancellationToken);
}
