using Memento.Core.Bridge.Methods;
using Memento.Core.Status;
using Microsoft.Extensions.DependencyInjection;

namespace Memento.Core.Bridge;

/// <summary>Registers the bridge router, every M0 method and the event publishers.</summary>
public static class BridgeServiceCollectionExtensions
{
    public static IServiceCollection AddMementoBridge(this IServiceCollection services)
    {
        services.AddSingleton<IBridgeHandler, AppVersionMethod>();
        services.AddSingleton<IBridgeHandler, OpenExternalMethod>();
        services.AddSingleton<IBridgeHandler, SettingsGetMethod>();
        services.AddSingleton<IBridgeHandler, SettingsSetMethod>();
        services.AddSingleton<IBridgeHandler, LibraryListMethod>();
        services.AddSingleton<IBridgeHandler, UiReadyMethod>();
        services.AddSingleton<BridgeRouter>();
        services.AddSingleton<BridgeEventPublisher>();
        services.AddSingleton<FooterStatusService>();
        return services;
    }
}
