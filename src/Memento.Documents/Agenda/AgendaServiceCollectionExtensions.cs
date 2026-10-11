using Memento.Core.Models;
using Memento.Core.Workers;
using Memento.Documents.Agenda.Ocr;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Memento.Documents.Agenda;

/// <summary>
/// Registers agenda import: the parsers, the OCR engines (Windows OCR first, Tesseract as the alternative) and
/// <see cref="AgendaImporter"/>. Tesseract runs once the model manager has its language data and Memento.Worker can
/// run it (<see cref="WorkerTesseractRunner"/>, unless another <see cref="ITesseractRunner"/> is registered).
/// </summary>
public static class AgendaServiceCollectionExtensions
{
    public static IServiceCollection AddAgendaImport(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IOcrEngine, WindowsOcrEngine>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IOcrEngine, TesseractOcrEngine>(sp => new TesseractOcrEngine(
            sp.GetService<IModelManager>(),
            sp.GetService<ITesseractRunner>() ?? (sp.GetService<WorkerClient>() is { } workers ? new WorkerTesseractRunner(workers) : null),
            sp.GetService<ILogger<TesseractOcrEngine>>())));
        services.TryAddSingleton(sp => new AgendaImporter(
            AgendaImporter.CreateParsers(sp.GetServices<IOcrEngine>()),
            sp.GetService<ILogger<AgendaImporter>>()));
        return services;
    }
}
