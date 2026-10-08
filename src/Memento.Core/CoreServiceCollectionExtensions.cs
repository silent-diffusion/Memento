using Memento.Core.Audio;
using Memento.Core.Engines;
using Memento.Core.Library;
using Memento.Core.Models;
using Memento.Core.Processing;
using Memento.Core.Projects;
using Memento.Core.Recording;
using Memento.Core.Recording.Simulation;
using Memento.Core.Recovery;
using Memento.Core.Status;
using Memento.Core.Transcripts;
using Memento.Core.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Memento.Core;

/// <summary>Registers the project store, library index, recording pipeline, recovery, models, engines and transcripts.</summary>
public static class CoreServiceCollectionExtensions
{
    /// <summary>
    /// Everything except the capture engine: register one with <see cref="AddSimulatedAudio"/> or
    /// Memento.Audio's own extension. More <see cref="IAudioEncoder"/>s can be added; the WAV pass-through is the fallback.
    /// Transcription and speaker stages come from Memento.Transcription; without them a stored recording only gets
    /// <c>optimize</c>. Defaults registered with TryAdd (models folder, resource probe, worker location) can be replaced
    /// before this call.
    /// </summary>
    public static IServiceCollection AddMementoLibrary(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ILibraryLocation, SettingsLibraryLocation>();
        services.TryAddSingleton<RecordingStatusBoard>();
        services.TryAddSingleton<LibraryAvailability>();
        services.TryAddSingleton<LibraryOpener>();
        services.TryAddSingleton(new RecordingCoordinatorOptions());
        services.AddSingleton<IProjectStore, ProjectStore>();
        services.AddSingleton<TranscriptStore>();
        services.AddSingleton<SqliteLibraryIndex>();
        services.AddSingleton<ILibraryIndex>(sp => sp.GetRequiredService<SqliteLibraryIndex>());
        services.AddSingleton<ProjectCatalog>();
        services.AddSingleton<ProjectService>();
        services.AddSingleton<IAudioEncoder, PassThroughWavEncoder>();
        services.AddSingleton<ITrackFinalizer, TrackFinalizer>();
        services.AddSingleton<ProjectFinalizationService>();
        services.AddSingleton<OptimizeStage>();
        services.AddSingleton<IProcessingStage>(sp => sp.GetRequiredService<OptimizeStage>());
        services.AddSingleton<StageStatusWriter>();
        services.AddSingleton<ProcessingOrchestrator>();
        services.AddSingleton<RecordingCoordinator>();
        services.AddSingleton<RecoveryService>();

        services.TryAddSingleton(ModelCatalog.Default);
        services.TryAddSingleton(ModelStoreOptions.Default);
        services.TryAddSingleton<ModelDownloadClient>();
        services.TryAddSingleton<ModelManager>();
        services.TryAddSingleton<IModelManager>(sp => sp.GetRequiredService<ModelManager>());
        services.TryAddSingleton<IResourceProbe, NullResourceProbe>();
        services.TryAddSingleton<ProcessingGate>();
        services.TryAddSingleton<EngineSelector>();
        services.TryAddSingleton<EngineStatusService>();
        services.TryAddSingleton(WorkerLocation.Default);
        services.TryAddSingleton<IWorkerLauncher, ProcessWorkerLauncher>();
        services.TryAddSingleton<WorkerClient>();
        services.AddSingleton<TranscriptWriter>();
        services.AddSingleton<TranscriptService>();
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
