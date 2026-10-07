using Memento.Documents.Model.Modules;
using Memento.Documents.Render;
using Memento.Documents.Styling;
using Memento.Documents.Templates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Memento.Documents.Export;

/// <summary>Registers the document model services: catalog, template and style stores, renderer and exporters.</summary>
public static class DocumentsServiceCollectionExtensions
{
    /// <summary>
    /// Adds the documents services. <paramref name="templatesFolder"/> and <paramref name="stylesFolder"/> hold the user's
    /// templates and styles (for example <c>%LOCALAPPDATA%\Memento\templates</c> and <c>…\styles</c>). PDF export uses an
    /// <see cref="IPdfPrinter"/> when the host registers one.
    /// </summary>
    public static IServiceCollection AddDocumentModel(this IServiceCollection services, string templatesFolder, string stylesFolder)
    {
        services.TryAddSingleton(ModuleCatalog.Default);
        services.TryAddSingleton<ITemplateStore>(sp => new FileTemplateStore(templatesFolder, sp.GetService<ILogger<FileTemplateStore>>()));
        services.TryAddSingleton<IStyleStore>(sp => new FileStyleStore(stylesFolder, sp.GetService<ILogger<FileStyleStore>>()));
        services.TryAddSingleton(sp => new DocumentHtmlRenderer(sp.GetRequiredService<ModuleCatalog>()));
        services.TryAddSingleton(sp => new DocxExporter(sp.GetRequiredService<ModuleCatalog>()));
        services.TryAddSingleton(sp => new MarkdownExporter(sp.GetRequiredService<ModuleCatalog>()));
        services.TryAddSingleton(sp => new DocumentExporter(
            sp.GetService<IPdfPrinter>(),
            sp.GetRequiredService<DocxExporter>(),
            sp.GetRequiredService<MarkdownExporter>(),
            sp.GetRequiredService<DocumentHtmlRenderer>()));
        return services;
    }
}
