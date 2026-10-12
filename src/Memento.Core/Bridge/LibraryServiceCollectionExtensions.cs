using Memento.Core.Bridge.Methods;
using Microsoft.Extensions.DependencyInjection;

namespace Memento.Core.Bridge;

/// <summary>
/// The 2.1 Library bridge methods (BRIDGE.md "2.1 — Folders, dates and Delete audio"): folders and Delete audio. Called by
/// <see cref="BridgeServiceCollectionExtensions.AddMementoBridge"/>; their services come from <c>AddMementoLibrary</c>.
/// </summary>
public static class LibraryServiceCollectionExtensions
{
    public static IServiceCollection AddMementoLibraryBridge(this IServiceCollection services)
    {
        services.AddSingleton<IBridgeHandler, FoldersListMethod>();
        services.AddSingleton<IBridgeHandler, FoldersCreateMethod>();
        services.AddSingleton<IBridgeHandler, FoldersRenameMethod>();
        services.AddSingleton<IBridgeHandler, FoldersMoveMethod>();
        services.AddSingleton<IBridgeHandler, FoldersDeleteMethod>();
        services.AddSingleton<IBridgeHandler, LibraryMoveToFolderMethod>();
        services.AddSingleton<IBridgeHandler, AudioRemoveMethod>();
        services.AddSingleton<IBridgeHandler, AudioRemoveEstimateMethod>();
        return services;
    }
}
