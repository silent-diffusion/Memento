using Memento.Documents.Export;
using Memento.Generation;
using Microsoft.Extensions.DependencyInjection;

namespace Memento.App.Hosting;

/// <summary>
/// The host side of M4: documents, templates, styles, providers and generation (Memento.Generation), with PDF export
/// printed by a hidden WebView2.
/// </summary>
internal static class M4HostServices
{
    public static IServiceCollection AddMementoM4Host(this IServiceCollection services)
    {
        // Registered before the document model so its exporter picks the printer up.
        services.AddSingleton<IPdfPrinter, WebViewPdfPrinter>();
        services.AddMementoM4();
        return services;
    }
}
