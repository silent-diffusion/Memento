using Memento.Core;
using Memento.Core.Bridge;
using Memento.Core.Export;
using Memento.Core.Host;
using Memento.Core.Library;
using Memento.Core.Models;
using Memento.Core.Processing;
using Memento.Core.Projects;
using Memento.Core.Recording;
using Memento.Core.Recording.Simulation;
using Memento.Core.Secrets;
using Memento.Core.Settings;
using Memento.Video.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Memento.Video.Tests.Hardware;

/// <summary>
/// The app's composition for the recording path (Core's registrations, the simulated audio engine in real time, and
/// Memento.Video's capture and files) with every folder in a temporary directory, so a hardware test records through
/// the real coordinator, video stage and export composer without touching Memento's data root. Nothing is played.
/// </summary>
internal sealed class OrchestratorHost : IDisposable
{
    private readonly ServiceProvider _services;

    public OrchestratorHost(string folder)
    {
        Folder = folder;
        Directory.CreateDirectory(Path.Combine(folder, "Library"));
        var collection = new ServiceCollection();
        collection.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        collection.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        collection.AddSingleton<IAppInfo>(new HostInfo());
        collection.AddSingleton<IThemeState>(new HostInfo());
        collection.AddSingleton<IExternalLauncher>(new HostInfo());
        collection.AddSingleton<IUiLifecycle>(new HostInfo());
        collection.AddSingleton<IFolderPicker>(new HostInfo());
        collection.AddSingleton<IBridgeEventSink>(Events);
        collection.AddSingleton<IFreeSpaceProbe, DriveFreeSpaceProbe>();
        collection.AddSingleton<ILibraryLocation>(new Library(Path.Combine(folder, "Library")));
        collection.AddSingleton<ISettingsStore>(sp => new JsonSettingsStore(Path.Combine(folder, "settings.json"), sp.GetRequiredService<ILogger<JsonSettingsStore>>()));
        collection.AddSingleton(new ModelStoreOptions(Path.Combine(folder, "models")));
        collection.AddSingleton(new ExportJournalOptions(Path.Combine(folder, "exports-running.json")));
        collection.AddSingleton(new SecretStoreOptions(Path.Combine(folder, "secrets.bin")));
        collection.AddMementoBridge();
        collection.AddMementoLibrary();
        collection.AddSimulatedAudio(new SimulatedEngineOptions { Speed = 1 });
        collection.AddMementoVideo();
        _services = collection.BuildServiceProvider();
    }

    public string Folder { get; }

    public EventLog Events { get; } = new();

    public RecordingCoordinator Recordings => _services.GetRequiredService<RecordingCoordinator>();

    public ProcessingOrchestrator Processing => _services.GetRequiredService<ProcessingOrchestrator>();

    public IProjectStore Store => _services.GetRequiredService<IProjectStore>();

    public T Get<T>()
        where T : notnull => _services.GetRequiredService<T>();

    public void Dispose()
    {
        Recordings.ShutdownAsync(CancellationToken.None).GetAwaiter().GetResult();
        Processing.StopAsync().GetAwaiter().GetResult();
        _services.Dispose();
    }

    internal sealed class EventLog : IBridgeEventSink
    {
        private readonly List<string> _posted = [];

        public IReadOnlyList<string> Posted
        {
            get
            {
                lock (_posted)
                {
                    return [.. _posted];
                }
            }
        }

        public void Post(string eventJson)
        {
            lock (_posted)
            {
                _posted.Add(eventJson);
            }
        }
    }

    private sealed class Library(string root) : ILibraryLocation
    {
        public string Root { get; } = root;
    }

    private sealed class HostInfo : IAppInfo, IThemeState, IExternalLauncher, IUiLifecycle, IFolderPicker
    {
        public string Version => "2.0.0-test";

        public string OsVersion => Environment.OSVersion.VersionString;

        public bool IsDark => false;

        public bool TryOpen(Uri uri) => false;

        public void NotifyReady()
        {
        }

        public Task<string?> PickAsync(string title, string? initialPath, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    }
}
