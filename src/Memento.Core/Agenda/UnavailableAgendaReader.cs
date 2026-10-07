using Memento.Core.Bridge;

namespace Memento.Core.Agendas;

/// <summary>The reader when Memento.Documents is not registered (tools, some tests): every import is refused clearly.</summary>
public sealed class UnavailableAgendaReader : IAgendaReader
{
    public Task<AgendaReading> ReadFileAsync(string path, CancellationToken cancellationToken) => throw Unavailable();

    public Task<AgendaReading> ReadTextAsync(string text, CancellationToken cancellationToken) => throw Unavailable();

    private static BridgeException Unavailable() =>
        new(
            DomainErrorCodes.AgendaUnsupportedFormat,
            "Agenda import is not set up in this build of Memento. Nothing was imported. Add the items by hand in the Details sheet.");
}
