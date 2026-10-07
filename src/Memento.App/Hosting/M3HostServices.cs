using Memento.Audio.Adapters;
using Memento.Core.Host;
using Memento.Documents.Agenda.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Memento.App.Hosting;

/// <summary>
/// The host side of M3 that Core cannot provide itself: agenda parsing and OCR (Memento.Documents), the Media
/// Foundation decoder for imports and exports (Memento.Audio) and the Windows file picker. Core's own M3 services
/// come with <c>AddMementoBridge</c>.
/// </summary>
internal static class M3HostServices
{
    public static IServiceCollection AddMementoM3Host(this IServiceCollection services)
    {
        services.AddAgendaReader();
        services.AddMediaFoundationMedia();
        services.AddSingleton<IFilePicker, WpfFilePicker>();
        return services;
    }
}
