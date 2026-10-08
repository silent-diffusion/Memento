using System.Text.Json;
using Memento.Core.Bridge;
using Memento.Core.Engines;
using Memento.Core.Host;
using Memento.Core.Library;
using Memento.Core.Models;
using Memento.Core.Processing;
using Memento.Core.Projects;
using Memento.Core.Recording;
using Memento.Core.Recording.Simulation;
using Memento.Core.Recovery;
using Memento.Core.Settings;
using Memento.Core.Transcripts;
using Memento.Core.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Memento.Core.Tests.Fakes;

/// <summary>
/// The production registrations (<see cref="BridgeServiceCollectionExtensions"/>, <see cref="CoreServiceCollectionExtensions"/>)
/// wired to fakes, a temporary library folder and the simulated engine in manual mode (Speed 0).
/// </summary>
internal sealed class BridgeTestHost : IDisposable
{
    private readonly ServiceProvider _services;
    private readonly TempDirectory _directory;
    private readonly bool _ownsDirectory;

    /// <param name="configure">Extra registrations after the production ones (the last registration wins).</param>
    public BridgeTestHost(SimulatedEngineOptions? engine = null, TempDirectory? directory = null, Action<IServiceCollection>? configure = null)
    {
        // A folder passed in belongs to the test (several hosts can share it, as successive app runs).
        _ownsDirectory = directory is null;
        _directory = directory ?? new TempDirectory();
        SettingsFile = _directory.File("settings.json");
        Library = new FakeLibraryLocation(_directory.File("Library"));
        var collection = new ServiceCollection();
        collection.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        collection.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        collection.AddSingleton<IAppInfo>(AppInfo);
        collection.AddSingleton<IThemeState>(Theme);
        collection.AddSingleton<IExternalLauncher>(Launcher);
        collection.AddSingleton<IUiLifecycle>(Lifecycle);
        collection.AddSingleton<IBridgeEventSink>(Sink);
        collection.AddSingleton<IFreeSpaceProbe>(FreeSpace);
        collection.AddSingleton<IFolderPicker>(FolderPicker);
        collection.AddSingleton<ILibraryLocation>(Library);
        collection.AddSingleton(new RecordingCoordinatorOptions
        {
            DiskSampleInterval = TimeSpan.FromMilliseconds(20),
            StateTickInterval = TimeSpan.FromMilliseconds(50),
            LevelsMinInterval = TimeSpan.Zero,
        });
        collection.AddSingleton<ISettingsStore>(sp => new JsonSettingsStore(SettingsFile, sp.GetRequiredService<ILogger<JsonSettingsStore>>()));
        collection.AddSingleton(new ModelStoreOptions(_directory.File("models")));
        collection.AddSingleton(new Memento.Core.Export.ExportJournalOptions(_directory.File("exports-running.json")));
        collection.AddSingleton<IResourceProbe>(Probe);
        collection.AddSingleton<IWorkerLauncher>(Workers);
        collection.AddMementoBridge();
        collection.AddMementoLibrary();
        // Manual time produces audio in bursts, so give the ring buffers room for the longest test (200 s).
        collection.AddSimulatedAudio(engine ?? new SimulatedEngineOptions { Speed = 0, BufferBlocks = 20_000 });
        configure?.Invoke(collection);
        _services = collection.BuildServiceProvider();
    }

    public string SettingsFile { get; }

    public FakeLibraryLocation Library { get; }

    public string Root => _directory.Path;

    public FakeAppInfo AppInfo { get; } = new();

    public FakeThemeState Theme { get; } = new();

    public FakeExternalLauncher Launcher { get; } = new();

    public FakeUiLifecycle Lifecycle { get; } = new();

    public RecordingEventSink Sink { get; } = new();

    public FakeFreeSpaceProbe FreeSpace { get; } = new();

    public FakeFolderPicker FolderPicker { get; } = new();

    public FakeResourceProbe Probe { get; } = new();

    /// <summary>The worker stand-in every job of this host runs in.</summary>
    public ScriptedWorkerLauncher Workers { get; } = new();

    public string ModelsRoot => _directory.File("models");

    public ModelManager Models => _services.GetRequiredService<ModelManager>();

    public ProcessingGate Gate => _services.GetRequiredService<ProcessingGate>();

    public TranscriptStore Transcripts => _services.GetRequiredService<TranscriptStore>();

    public BridgeRouter Router => _services.GetRequiredService<BridgeRouter>();

    public ISettingsStore Settings => _services.GetRequiredService<ISettingsStore>();

    public IProjectStore Store => _services.GetRequiredService<IProjectStore>();

    public ILibraryIndex Index => _services.GetRequiredService<ILibraryIndex>();

    public RecordingCoordinator Recordings => _services.GetRequiredService<RecordingCoordinator>();

    public RecoveryService Recovery => _services.GetRequiredService<RecoveryService>();

    public ProcessingOrchestrator Processing => _services.GetRequiredService<ProcessingOrchestrator>();

    public SimulatedRecordingEngine Engine => _services.GetRequiredService<SimulatedRecordingEngine>();

    public SimulatedAudioSourceProvider Sources => _services.GetRequiredService<SimulatedAudioSourceProvider>();

    /// <summary>The session the last <c>recording.start</c> opened.</summary>
    public SimulatedRecordingSession Session => Engine.LastSession ?? throw new InvalidOperationException("No session was started.");

    public T Get<T>()
        where T : notnull => _services.GetRequiredService<T>();

    /// <summary>Sends a raw message and parses the response.</summary>
    public async Task<JsonElement> SendAsync(string message)
    {
        var response = await Router.HandleAsync(message, CancellationToken.None);
        using var document = JsonDocument.Parse(response);
        return document.RootElement.Clone();
    }

    /// <summary>Calls <paramref name="method"/> with JSON <paramref name="parameters"/>; returns the whole response.</summary>
    public Task<JsonElement> CallAsync(string method, string parameters = "{}") =>
        SendAsync($$"""{"id":7,"method":"{{method}}","params":{{parameters}}}""");

    /// <summary>Calls and returns <c>result</c>, failing the test with the error if there is one.</summary>
    public async Task<JsonElement> ResultAsync(string method, string parameters = "{}")
    {
        var response = await CallAsync(method, parameters);
        if (response.TryGetProperty("error", out var error))
        {
            throw new Xunit.Sdk.XunitException($"{method} failed: {error}");
        }

        return response.GetProperty("result");
    }

    /// <summary>Stops the background work of any session before the folder goes away.</summary>
    public void Dispose()
    {
        Recordings.ShutdownAsync(CancellationToken.None).GetAwaiter().GetResult();
        Processing.StopAsync().GetAwaiter().GetResult();
        _services.Dispose();
        if (_ownsDirectory)
        {
            _directory.Dispose();
        }
    }

    /// <summary>Disposes the services but keeps the folder (simulates the app exiting; a new host can reopen it).</summary>
    public void DisposeServicesOnly() => _services.Dispose();
}
