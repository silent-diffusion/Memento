using Memento.Core.Agendas;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Memento.Documents.Agenda.Hosting;

/// <summary>Agenda import for the app: the parsers and OCR (<see cref="AgendaServiceCollectionExtensions.AddAgendaImport"/>) behind Core's <see cref="IAgendaReader"/>.</summary>
public static class AgendaReaderServiceCollectionExtensions
{
    public static IServiceCollection AddAgendaReader(this IServiceCollection services)
    {
        services.AddAgendaImport();
        services.Replace(ServiceDescriptor.Singleton<IAgendaReader, DocumentsAgendaReader>());
        return services;
    }
}
