using System.Text.Json;
using Memento.Core.Bridge;
using Memento.Core.Host;
using Memento.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Memento.Core.Tests.Fakes;

/// <summary>The production bridge registration (<see cref="BridgeServiceCollectionExtensions"/>) wired to fakes.</summary>
internal sealed class BridgeTestHost : IDisposable
{
    private readonly ServiceProvider _services;
    private readonly TempDirectory _directory = new();

    public BridgeTestHost()
    {
        SettingsFile = _directory.File("settings.json");
        var collection = new ServiceCollection();
        collection.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        collection.AddSingleton<IAppInfo>(AppInfo);
        collection.AddSingleton<IThemeState>(Theme);
        collection.AddSingleton<IExternalLauncher>(Launcher);
        collection.AddSingleton<IUiLifecycle>(Lifecycle);
        collection.AddSingleton<IBridgeEventSink>(Sink);
        collection.AddSingleton<IFreeSpaceProbe>(FreeSpace);
        collection.AddSingleton<ISettingsStore>(sp => new JsonSettingsStore(SettingsFile, sp.GetRequiredService<ILogger<JsonSettingsStore>>()));
        collection.AddMementoBridge();
        _services = collection.BuildServiceProvider();
    }

    public string SettingsFile { get; }

    public FakeAppInfo AppInfo { get; } = new();

    public FakeThemeState Theme { get; } = new();

    public FakeExternalLauncher Launcher { get; } = new();

    public FakeUiLifecycle Lifecycle { get; } = new();

    public RecordingEventSink Sink { get; } = new();

    public FakeFreeSpaceProbe FreeSpace { get; } = new();

    public BridgeRouter Router => _services.GetRequiredService<BridgeRouter>();

    public ISettingsStore Settings => _services.GetRequiredService<ISettingsStore>();

    public T Get<T>()
        where T : notnull => _services.GetRequiredService<T>();

    /// <summary>Sends a raw message and parses the response.</summary>
    public async Task<JsonElement> SendAsync(string message)
    {
        var response = await Router.HandleAsync(message, CancellationToken.None);
        using var document = JsonDocument.Parse(response);
        return document.RootElement.Clone();
    }

    public void Dispose()
    {
        _services.Dispose();
        _directory.Dispose();
    }
}
