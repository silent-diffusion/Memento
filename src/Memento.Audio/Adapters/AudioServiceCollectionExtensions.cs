using Memento.Audio.Codecs;
using Memento.Audio.Sources;
using Memento.Core.Audio;
using Memento.Core.Host;
using Memento.Core.Recording;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Memento.Audio.Adapters;

/// <summary>Registers the audio layer with Core. Call after <c>AddMementoLibrary</c>: the last registration wins.</summary>
public static class AudioServiceCollectionExtensions
{
    /// <summary>Real capture: WASAPI sources and engine (replaces a simulated engine registered earlier).</summary>
    public static IServiceCollection AddWasapiAudio(this IServiceCollection services, WasapiEngineOptions? options = null)
    {
        services.AddSingleton(options ?? new WasapiEngineOptions());
        services.AddSingleton(sp => new AudioSourceEnumerator(sp.GetRequiredService<ILogger<AudioSourceEnumerator>>()));
        services.AddSingleton<WasapiAudioSourceProvider>();
        services.AddSingleton<IAudioSourceProvider>(sp => sp.GetRequiredService<WasapiAudioSourceProvider>());
        services.AddSingleton<WasapiRecordingEngine>();
        services.AddSingleton<IRecordingEngine>(sp => sp.GetRequiredService<WasapiRecordingEngine>());
        return services;
    }

    /// <summary>
    /// Media Foundation storage for any engine: lossless FLAC finalize (tracks, mix, peaks), the FLAC, AAC and MP3
    /// encoders the <c>optimize</c> stage chooses from, and the decoder that verifies what it wrote.
    /// </summary>
    public static IServiceCollection AddMediaFoundationStorage(this IServiceCollection services)
    {
        services.AddSingleton(sp => new MediaFoundationFlacEncoder(sp.GetRequiredService<IFreeSpaceProbe>(), sp.GetRequiredService<ILogger<MediaFoundationFlacEncoder>>()));
        services.AddSingleton(sp => new MediaFoundationLossyEncoder(sp.GetRequiredService<ILogger<MediaFoundationLossyEncoder>>()));
        services.AddSingleton<IAudioEncoder>(sp => MediaFoundationAudioEncoder.Flac(sp.GetRequiredService<MediaFoundationFlacEncoder>()));
        services.AddSingleton<IAudioEncoder>(sp => MediaFoundationAudioEncoder.Aac(sp.GetRequiredService<MediaFoundationLossyEncoder>()));
        services.AddSingleton<IAudioEncoder>(sp => MediaFoundationAudioEncoder.Mp3(sp.GetRequiredService<MediaFoundationLossyEncoder>()));
        services.AddSingleton<IAudioFileVerifier, MediaFoundationAudioVerifier>();
        services.AddSingleton<MediaFoundationTrackFinalizer>();
        services.AddSingleton<ITrackFinalizer>(sp => sp.GetRequiredService<MediaFoundationTrackFinalizer>());
        return services;
    }
}
