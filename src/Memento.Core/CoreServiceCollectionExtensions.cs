using Memento.Core.Audio;
using Memento.Core.Library;
using Memento.Core.Projects;
using Memento.Core.Recording;
using Memento.Core.Recording.Simulation;
using Memento.Core.Recovery;
using Memento.Core.Status;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Memento.Core;

/// <summary>Registers the project store, library index, recording pipeline and recovery.</summary>
public static class CoreServiceCollectionExtensions
{
    /// <summary>
    /// Everything except the capture engine: register one with <see cref="AddSimulatedAudio"/> or
    /// Memento.Audio's own extension. More <see cref="IAudioEncoder"/>s can be added; the WAV pass-through is the fallback.
    /// </summary>
    public static IServiceCollection AddMementoLibrary(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ILibraryLocation, SettingsLibraryLocation>();
        services.TryAddSingleton<RecordingStatusBoard>();
        services.TryAddSingleton(new RecordingCoordinatorOptions());
        services.AddSingleton<IProjectStore, ProjectStore>();
        services.AddSingleton<SqliteLibraryIndex>();
        services.AddSingleton<ILibraryIndex>(sp => sp.GetRequiredService<SqliteLibraryIndex>());
        services.AddSingleton<ProjectCatalog>();
        services.AddSingleton<ProjectService>();
        services.AddSingleton<IAudioEncoder, PassThroughWavEncoder>();
        services.AddSingleton<ITrackFinalizer, TrackFinalizer>();
        services.AddSingleton<ProjectFinalizationService>();
        services.AddSingleton<RecordingCoordinator>();
        services.AddSingleton<RecoveryService>();
        return services;
    }

    /// <summary>The simulated engine and sources (tests, soak runs, <c>--simulate-audio</c>).</summary>
    public static IServiceCollection AddSimulatedAudio(this IServiceCollection services, SimulatedEngineOptions options)
    {
        services.AddSingleton(options);
        services.AddSingleton<SimulatedAudioSourceProvider>();
        services.AddSingleton<IAudioSourceProvider>(sp => sp.GetRequiredService<SimulatedAudioSourceProvider>());
        services.AddSingleton<SimulatedRecordingEngine>();
        services.AddSingleton<IRecordingEngine>(sp => sp.GetRequiredService<SimulatedRecordingEngine>());
        return services;
    }
}
