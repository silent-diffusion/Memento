using Memento.Core.Video;
using Microsoft.Extensions.DependencyInjection;

namespace Memento.Video.Hosting;

/// <summary>Replaces Core's stand-ins with the real video capture and files (call after <c>AddMementoLibrary</c>).</summary>
public static class VideoServiceCollectionExtensions
{
    public static IServiceCollection AddMementoVideo(this IServiceCollection services)
    {
        services.AddSingleton<IVideoCapture, MediaFoundationVideoCapture>();
        services.AddSingleton<IVideoFiles, MediaFoundationVideoFiles>();
        return services;
    }
}
