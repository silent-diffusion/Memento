using System.Text.Json;
using Memento.Core.Settings;
using Memento.Core.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Memento.Core.Tests.Settings;

public sealed class JsonSettingsStoreTests : IDisposable
{
    private readonly TempDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    private string SettingsPath => _directory.File("settings.json");

    private JsonSettingsStore CreateStore() => new(SettingsPath, NullLogger<JsonSettingsStore>.Instance);

    [Fact]
    public async Task MissingFileYieldsDefaultsAndWritesNothing()
    {
        using var store = CreateStore();

        var settings = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(AppSettings.CurrentSchemaVersion, settings.SchemaVersion);
        Assert.Equal(ThemePreference.System, settings.Theme);
        Assert.Equal(ListDensity.Comfortable, settings.ListDensity);
        Assert.Null(settings.LibraryPath);
        Assert.Equal(AppPaths.DefaultLibrary, settings.EffectiveLibraryPath);
        Assert.False(File.Exists(SettingsPath));
    }

    [Fact]
    public async Task UpdateRoundTripsThroughANewStore()
    {
        using (var store = CreateStore())
        {
            await store.LoadAsync(CancellationToken.None);
            await store.UpdateAsync(s => s with { Theme = ThemePreference.Dark, ListDensity = ListDensity.Compact }, CancellationToken.None);
        }

        using var reopened = CreateStore();
        var settings = await reopened.LoadAsync(CancellationToken.None);

        Assert.Equal(ThemePreference.Dark, settings.Theme);
        Assert.Equal(ListDensity.Compact, settings.ListDensity);
    }

    [Fact]
    public async Task WrittenFileCarriesSchemaVersionAndCamelCaseNames()
    {
        using var store = CreateStore();
        await store.UpdateAsync(s => s with { Theme = ThemePreference.Light }, CancellationToken.None);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(SettingsPath));
        var root = document.RootElement;

        Assert.Equal(AppSettings.CurrentSchemaVersion, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("light", root.GetProperty("theme").GetString());
        Assert.Equal("comfortable", root.GetProperty("listDensity").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("libraryPath").ValueKind);
        Assert.False(root.TryGetProperty("effectiveLibraryPath", out _));
    }

    [Fact]
    public async Task UnknownFieldsArePreservedOnRoundTrip()
    {
        await File.WriteAllTextAsync(SettingsPath, """
            {
              "schemaVersion": 1,
              "theme": "dark",
              "futureFlag": true,
              "recording": { "checkpointSeconds": 30, "sources": ["mic", "system"] },
              "listDensity": "comfortable"
            }
            """);
        using var store = CreateStore();
        await store.LoadAsync(CancellationToken.None);

        await store.UpdateAsync(s => s with { ListDensity = ListDensity.Compact }, CancellationToken.None);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(SettingsPath));
        var root = document.RootElement;
        Assert.True(root.GetProperty("futureFlag").GetBoolean());
        Assert.Equal(30, root.GetProperty("recording").GetProperty("checkpointSeconds").GetInt32());
        Assert.Equal(["mic", "system"], root.GetProperty("recording").GetProperty("sources").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal("compact", root.GetProperty("listDensity").GetString());
        Assert.Equal("dark", root.GetProperty("theme").GetString());
    }

    [Fact]
    public async Task WriteIsAtomicAndLeavesNoTemporaryFile()
    {
        using var store = CreateStore();
        await File.WriteAllTextAsync(SettingsPath + ".tmp", "left over from a crash");

        await store.UpdateAsync(s => s with { Theme = ThemePreference.Dark }, CancellationToken.None);

        Assert.False(File.Exists(SettingsPath + ".tmp"));
        Assert.Single(Directory.GetFiles(_directory.Path));
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(SettingsPath));
        Assert.Equal("dark", document.RootElement.GetProperty("theme").GetString());
    }

    [Fact]
    public async Task AFailedWriteLeavesTheExistingFileAndStateUntouched()
    {
        using var store = CreateStore();
        await store.UpdateAsync(s => s with { Theme = ThemePreference.Light }, CancellationToken.None);
        var before = await File.ReadAllTextAsync(SettingsPath);

        // A directory where the temporary file should go makes the write fail before the move.
        Directory.CreateDirectory(SettingsPath + ".tmp");

        await Assert.ThrowsAnyAsync<Exception>(() =>
            store.UpdateAsync(s => s with { Theme = ThemePreference.Dark }, CancellationToken.None));

        Assert.Equal(before, await File.ReadAllTextAsync(SettingsPath));
        Assert.Equal(ThemePreference.Light, store.Current.Theme);
    }

    [Fact]
    public async Task UnreadableFileIsKeptAsideAndDefaultsAreUsed()
    {
        await File.WriteAllTextAsync(SettingsPath, "{ this is not json");
        using var store = CreateStore();

        var settings = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(ThemePreference.System, settings.Theme);
        Assert.False(File.Exists(SettingsPath));
        var keptAside = Assert.Single(Directory.GetFiles(_directory.Path, "settings.json.unreadable-*"));
        Assert.Equal("{ this is not json", await File.ReadAllTextAsync(keptAside));
    }

    [Fact]
    public async Task UnsupportedValuesFallBackToDefaultsWhileOtherFieldsSurvive()
    {
        await File.WriteAllTextAsync(SettingsPath, """{"schemaVersion":1,"theme":"neon","listDensity":"compact"}""");
        using var store = CreateStore();

        var settings = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(ThemePreference.System, settings.Theme);
        Assert.Equal(ListDensity.Compact, settings.ListDensity);
    }

    [Fact]
    public async Task UpdateRejectsInvalidValuesWithoutWriting()
    {
        using var store = CreateStore();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.UpdateAsync(s => s with { Theme = "neon" }, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.UpdateAsync(s => s with { LibraryPath = "relative\\folder" }, CancellationToken.None));

        Assert.False(File.Exists(SettingsPath));
        Assert.Equal(ThemePreference.System, store.Current.Theme);
    }

    [Fact]
    public async Task ChangedIsRaisedOnlyWhenSomethingChanged()
    {
        using var store = CreateStore();
        var events = new List<SettingsChangedEventArgs>();
        store.Changed += (_, args) => events.Add(args);

        await store.UpdateAsync(s => s with { Theme = ThemePreference.Dark }, CancellationToken.None);
        await store.UpdateAsync(s => s with { Theme = ThemePreference.Dark }, CancellationToken.None);

        var change = Assert.Single(events);
        Assert.Equal(ThemePreference.System, change.Previous.Theme);
        Assert.Equal(ThemePreference.Dark, change.Current.Theme);
    }

    [Fact]
    public async Task CreatesTheDataFolderOnFirstWrite()
    {
        var nested = Path.Combine(_directory.Path, "Memento", "settings.json");
        using var store = new JsonSettingsStore(nested, NullLogger<JsonSettingsStore>.Instance);

        await store.UpdateAsync(s => s with { Theme = ThemePreference.Light }, CancellationToken.None);

        Assert.True(File.Exists(nested));
    }

    [Fact]
    public async Task OlderFilesWithoutSchemaVersionAreUpgradedOnWrite()
    {
        await File.WriteAllTextAsync(SettingsPath, """{"theme":"light"}""");
        using var store = CreateStore();
        await store.LoadAsync(CancellationToken.None);

        await store.UpdateAsync(s => s with { ListDensity = ListDensity.Compact }, CancellationToken.None);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(SettingsPath));
        Assert.Equal(AppSettings.CurrentSchemaVersion, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("light", document.RootElement.GetProperty("theme").GetString());
    }
}
