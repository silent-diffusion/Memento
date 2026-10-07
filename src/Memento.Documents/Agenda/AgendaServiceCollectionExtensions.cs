using Memento.Documents.Agenda.Ocr;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Memento.Documents.Agenda;

/// <summary>Registers agenda import: the parsers, the OCR engines (Windows OCR first, Tesseract as the alternative) and <see cref="AgendaImporter"/>.</summary>
public static class AgendaServiceCollectionExtensions
{
    public static IServiceCollection AddAgendaImport(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IOcrEngine, WindowsOcrEngine>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IOcrEngine, TesseractOcrEngine>(_ => new TesseractOcrEngine()));
        services.TryAddSingleton(sp => new AgendaImporter(
            AgendaImporter.CreateParsers(sp.GetServices<IOcrEngine>()),
            sp.GetService<ILogger<AgendaImporter>>()));
        return services;
    }
}
