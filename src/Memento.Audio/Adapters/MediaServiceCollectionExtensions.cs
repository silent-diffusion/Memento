using Memento.Core.Import;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Memento.Audio.Adapters;

/// <summary>M3 media import and export through Media Foundation.</summary>
public static class MediaServiceCollectionExtensions
{
    /// <summary>
    /// The Media Foundation decoder for <c>library.importMedia</c> and for exports to WAV or from lossy files. The
    /// encoders exports use come from <see cref="AudioServiceCollectionExtensions.AddMediaFoundationStorage"/>.
    /// </summary>
    public static IServiceCollection AddMediaFoundationMedia(this IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Singleton<IMediaDecoder, MediaFoundationMediaDecoder>());
        return services;
    }
}
