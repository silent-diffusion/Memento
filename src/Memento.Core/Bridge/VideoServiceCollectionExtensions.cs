using Memento.Core.Bridge.Methods;
using Microsoft.Extensions.DependencyInjection;

namespace Memento.Core.Bridge;

/// <summary>
/// The 2.0 video bridge methods (BRIDGE.md "Video (2.0)"). Called by <see cref="BridgeServiceCollectionExtensions.AddMementoBridge"/>;
/// the services they use come from <c>AddMementoLibrary</c>, and Memento.Video's <c>AddMementoVideo</c> replaces the
/// stand-ins for capture and files.
/// </summary>
public static class VideoServiceCollectionExtensions
{
    public static IServiceCollection AddMementoVideoBridge(this IServiceCollection services)
    {
        services.AddSingleton<IBridgeHandler, VideoListSourcesMethod>();
        services.AddSingleton<IBridgeHandler, VideoThumbnailMethod>();
        services.AddSingleton<IBridgeHandler, RecordingSetVideoSourceMethod>();
        services.AddSingleton<IBridgeHandler, RecordingSetPipMethod>();
        services.AddSingleton<IBridgeHandler, VideoOpenPrivacySettingsMethod>();
        services.AddSingleton<IBridgeHandler, VideoRemoveMethod>();
        services.AddSingleton<IBridgeHandler, RecordingSetVideoQualityMethod>();
        services.AddSingleton<IBridgeHandler, VideoRemoveEstimateMethod>();
        services.AddSingleton<IBridgeHandler, VideoAddSourceMethod>();
        services.AddSingleton<IBridgeHandler, VideoRemoveSourceMethod>();
        return services;
    }
}
