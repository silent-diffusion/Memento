using Memento.AI;
using Memento.AI.Http;
using Memento.Core;
using Memento.Core.Bridge;
using Memento.Core.Documents;
using Memento.Core.Secrets;
using Memento.Documents.Export;
using Memento.Generation.Ai;
using Memento.Generation.Bridge.Methods;
using Memento.Generation.Documents;
using Memento.Generation.Generation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Memento.Generation;

/// <summary>Registers M4: providers, the document store, templates and styles, generation, the Export dialog's Documents row and every M4 bridge method.</summary>
public static class M4ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the M4 services and bridge methods. Templates and styles the user makes live in <paramref name="templatesFolder"/>
    /// and <paramref name="stylesFolder"/> (default <c>%LOCALAPPDATA%\Memento\templates</c> and <c>…\styles</c>). PDF export
    /// uses the <c>IPdfPrinter</c> the app registers (a hidden WebView2).
    /// </summary>
    public static IServiceCollection AddMementoM4(this IServiceCollection services, string? templatesFolder = null, string? stylesFolder = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddDocumentModel(templatesFolder ?? Path.Combine(AppPaths.DataRoot, "templates"), stylesFolder ?? Path.Combine(AppPaths.DataRoot, "styles"));
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ISecretReader>(sp => new SecretStoreReader(sp.GetRequiredService<ISecretStore>()));

        // The HTTP client is created on the first cloud generation only; the local model never touches it.
        services.TryAddSingleton<AiHttpClient>();
        services.TryAddSingleton(sp => new Lazy<AiHttpClient>(sp.GetRequiredService<AiHttpClient>));
        services.TryAddSingleton<IAiProviderFactory, AiProviderFactory>();
        services.TryAddSingleton<ProviderRegistry>();

        services.TryAddSingleton<M4EventPublisher>();
        services.TryAddSingleton<ProjectDocumentStore>();
        services.TryAddSingleton<RecordingMaterialLoader>();
        services.TryAddSingleton<TemplateService>();
        services.TryAddSingleton<StyleService>();
        services.TryAddSingleton<DocumentService>();
        services.TryAddSingleton<GenerationPipeline>();
        services.TryAddSingleton<GenerationService>();
        services.TryAddSingleton<IDocumentExportSource, DocumentExportSource>();

        services.AddSingleton<IBridgeHandler, ModulesListMethod>();
        services.AddSingleton<IBridgeHandler, TemplatesListMethod>();
        services.AddSingleton<IBridgeHandler, TemplatesGetMethod>();
        services.AddSingleton<IBridgeHandler, TemplatesSaveMethod>();
        services.AddSingleton<IBridgeHandler, TemplatesDuplicateMethod>();
        services.AddSingleton<IBridgeHandler, TemplatesDeleteMethod>();
        services.AddSingleton<IBridgeHandler, TemplatesResetBuiltInMethod>();
        services.AddSingleton<IBridgeHandler, StylesListMethod>();
        services.AddSingleton<IBridgeHandler, StylesGetMethod>();
        services.AddSingleton<IBridgeHandler, StylesSaveMethod>();
        services.AddSingleton<IBridgeHandler, StylesDuplicateMethod>();
        services.AddSingleton<IBridgeHandler, StylesDeleteMethod>();
        services.AddSingleton<IBridgeHandler, StylesResetBuiltInMethod>();
        services.AddSingleton<IBridgeHandler, StylesSampleHtmlMethod>();
        services.AddSingleton<IBridgeHandler, ProvidersListMethod>();
        services.AddSingleton<IBridgeHandler, GenerationPreviewMethod>();
        services.AddSingleton<IBridgeHandler, GenerationPreviewHtmlMethod>();
        services.AddSingleton<IBridgeHandler, GenerationStartMethod>();
        services.AddSingleton<IBridgeHandler, GenerationConfirmMethod>();
        services.AddSingleton<IBridgeHandler, GenerationCancelMethod>();
        services.AddSingleton<IBridgeHandler, DocumentsListMethod>();
        services.AddSingleton<IBridgeHandler, DocumentsGetMethod>();
        services.AddSingleton<IBridgeHandler, DocumentsRenderHtmlMethod>();
        services.AddSingleton<IBridgeHandler, DocumentsCreateMethod>();
        services.AddSingleton<IBridgeHandler, DocumentsSaveEditMethod>();
        services.AddSingleton<IBridgeHandler, DocumentsRenameMethod>();
        services.AddSingleton<IBridgeHandler, DocumentsDuplicateMethod>();
        services.AddSingleton<IBridgeHandler, DocumentsDeleteMethod>();
        services.AddSingleton<IBridgeHandler, DocumentsMakeTemplateMethod>();
        services.AddSingleton<IBridgeHandler, DocumentsVersionsMethod>();
        services.AddSingleton<IBridgeHandler, DocumentsRestoreVersionMethod>();
        services.AddSingleton<IBridgeHandler, DocumentsExportMethod>();
        return services;
    }
}
