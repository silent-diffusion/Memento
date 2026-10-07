using System.Text;
using System.Text.Json;
using Memento.Core.Bridge;
using Memento.Core.Maintenance;
using Memento.Core.Secrets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;

namespace Memento.Core.Tests.M3;

/// <summary>Settings › General, Export, AI and privacy, Storage (BRIDGE.md M3), API keys and the startup entry.</summary>
public sealed class M3SettingsTests : IDisposable
{
    private static readonly string[] SrtOnly = ["srt"];

    private readonly M3Host _m3 = new();

    public void Dispose() => _m3.Dispose();

    [Fact]
    public async Task SettingsGetHasTheM3BlocksWithTheirDefaults()
    {
        var result = await _m3.ResultAsync("settings.get", new { });

        Assert.Equal("""{"startWithWindows":false,"keepRunningInTray":false,"language":"en"}""", result.GetProperty("general").GetRawText());
        Assert.Equal(
            """{"saveCopiesOutside":false,"defaultFolder":null,"askWhereEachTime":true,"createSubfolder":true,"defaults":{"audioMixed":{"on":true,"format":"flac","bitrateKbps":null},"tracks":{"on":false,"format":"flac","bitrateKbps":null},"transcript":{"on":true,"formats":["json","markdown"]},"documents":{"on":false,"documentIds":[],"format":"docx"},"details":{"on":false},"attachments":{"on":false}}}""",
            result.GetProperty("export").GetRawText());
        Assert.Equal(
            """{"enabled":false,"askBeforeSend":true,"keepRecord":true,"share":{"transcript":true,"details":true,"participants":true,"agenda":true,"highlights":true,"attachments":false},"providers":{"anthropic":{"hasKey":false},"openai":{"hasKey":false}}}""",
            result.GetProperty("ai").GetRawText());
        Assert.Equal("""{"reclaimOlderThanDays":null}""", result.GetProperty("storage").GetRawText());
    }

    [Fact]
    public async Task SettingsSetChangesOnlyTheFieldsItCarries()
    {
        var folder = _m3.Directory.File("Exports");
        var result = await _m3.ResultAsync("settings.set", new
        {
            general = new { keepRunningInTray = true },
            export = new { saveCopiesOutside = true, defaultFolder = folder, defaults = new { audioMixed = new { on = false, format = "mp3", bitrateKbps = 128 }, tracks = new { on = true, format = "wav" }, transcript = new { on = true, formats = SrtOnly }, documents = new { on = false }, details = new { on = true }, attachments = new { on = true } } },
            ai = new { enabled = true, share = new { attachments = true }, providers = new { anthropic = new { hasKey = true } } },
            storage = new { reclaimOlderThanDays = 90 },
        });

        Assert.True(result.GetProperty("general").GetProperty("keepRunningInTray").GetBoolean());
        Assert.False(result.GetProperty("general").GetProperty("startWithWindows").GetBoolean());
        Assert.Equal(folder, result.GetProperty("export").GetProperty("defaultFolder").GetString());
        Assert.True(result.GetProperty("export").GetProperty("askWhereEachTime").GetBoolean());
        Assert.Equal("mp3", result.GetProperty("export").GetProperty("defaults").GetProperty("audioMixed").GetProperty("format").GetString());
        Assert.True(result.GetProperty("ai").GetProperty("enabled").GetBoolean());
        Assert.True(result.GetProperty("ai").GetProperty("share").GetProperty("attachments").GetBoolean());
        Assert.True(result.GetProperty("ai").GetProperty("share").GetProperty("transcript").GetBoolean());
        Assert.False(result.GetProperty("ai").GetProperty("providers").GetProperty("anthropic").GetProperty("hasKey").GetBoolean());
        Assert.Equal(90, result.GetProperty("storage").GetProperty("reclaimOlderThanDays").GetInt32());

        // JSON null clears the nullable values; a block left out keeps them.
        var cleared = await _m3.ResultAsync("settings.set", new { export = new { defaultFolder = (string?)null }, storage = new { reclaimOlderThanDays = (int?)null } });
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("export").GetProperty("defaultFolder").ValueKind);
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("storage").GetProperty("reclaimOlderThanDays").ValueKind);
        Assert.True(cleared.GetProperty("export").GetProperty("saveCopiesOutside").GetBoolean());

        // Survives a restart of the settings store.
        var reloaded = await _m3.Host.Settings.LoadAsync(CancellationToken.None);
        Assert.True(reloaded.Ai.Enabled);
        Assert.Equal("srt", reloaded.Export.Defaults.Transcript.Formats.Single());
    }

    [Fact]
    public async Task AnOlderSettingsFileReadsTheM3BlocksWithDefaults()
    {
        File.WriteAllText(_m3.Host.SettingsFile, """{"schemaVersion":1,"theme":"dark","export":{"saveCopiesOutside":true,"defaults":{"tracks":{"on":true}}}}""");

        var settings = await _m3.Host.Settings.LoadAsync(CancellationToken.None);

        Assert.Equal("en", settings.General.Language);
        Assert.True(settings.Export.SaveCopiesOutside);
        Assert.True(settings.Export.AskWhereEachTime);
        Assert.True(settings.Export.Defaults.Tracks.On);
        Assert.Equal("flac", settings.Export.Defaults.Tracks.Format);
        Assert.NotNull(settings.Export.Defaults.Transcript.Formats);
        Assert.True(settings.Ai.AskBeforeSend);
        Assert.True(settings.Ai.Share.Transcript);
        Assert.Null(settings.Storage.ReclaimOlderThanDays);
    }

    public static TheoryData<string, string> InvalidValues => new()
    {
        { """{"general":{"language":"fr"}}""", "Language 'fr'" },
        { """{"export":{"defaultFolder":"Exports"}}""", "full path" },
        { """{"export":{"defaultFolder":12}}""", "export.defaultFolder" },
        { """{"export":{"defaults":{"audioMixed":{"on":true,"format":"ogg"},"tracks":{},"transcript":{},"documents":{},"details":{},"attachments":{}}}}""", "format 'ogg'" },
        { """{"export":{"defaults":{"audioMixed":{"on":true,"format":"mp3","bitrateKbps":64},"tracks":{},"transcript":{},"documents":{},"details":{},"attachments":{}}}}""", "96 to 320" },
        { """{"export":{"defaults":{"audioMixed":{},"tracks":{},"transcript":{"formats":["pdf"]},"documents":{},"details":{},"attachments":{}}}}""", "Transcript format 'pdf'" },
        { """{"storage":{"reclaimOlderThanDays":0}}""", "out of range" },
        { """{"storage":{"reclaimOlderThanDays":"soon"}}""", "whole number" },
    };

    [Theory]
    [MemberData(nameof(InvalidValues))]
    public async Task InvalidValuesChangeNothing(string parameters, string expected)
    {
        var before = File.Exists(_m3.Host.SettingsFile) ? File.ReadAllText(_m3.Host.SettingsFile) : null;

        var response = await _m3.Host.CallAsync("settings.set", parameters);

        var error = response.GetProperty("error");
        Assert.Equal(DomainErrorCodes.SettingsInvalidValue, error.GetProperty("code").GetString());
        Assert.Contains(expected, error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal(before, File.Exists(_m3.Host.SettingsFile) ? File.ReadAllText(_m3.Host.SettingsFile) : null);
    }

    [Fact]
    public async Task UnknownFieldsInAnM3BlockAreRejected()
    {
        var response = await _m3.Host.CallAsync("settings.set", """{"general":{"autoUpdate":true}}""");

        Assert.Equal(BridgeErrorCodes.InvalidParams, response.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task AKeyIsStoredEncryptedAndNeverReturned()
    {
        const string key = "sk-test-0123456789abcdefABCDEF";

        var set = await _m3.ResultAsync("ai.setKey", new { provider = "anthropic", key });
        var settings = await _m3.ResultAsync("settings.get", new { });

        Assert.Equal("""{"hasKey":true}""", set.GetRawText());
        Assert.True(settings.GetProperty("ai").GetProperty("providers").GetProperty("anthropic").GetProperty("hasKey").GetBoolean());
        Assert.False(settings.GetProperty("ai").GetProperty("providers").GetProperty("openai").GetProperty("hasKey").GetBoolean());
        Assert.DoesNotContain(key, settings.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain(key, File.Exists(_m3.Host.SettingsFile) ? File.ReadAllText(_m3.Host.SettingsFile) : string.Empty, StringComparison.Ordinal);

        var bytes = File.ReadAllBytes(_m3.Directory.File("secrets.bin"));
        Assert.Equal("MSEC"u8.ToArray(), bytes[..4]);
        Assert.Equal(DpapiSecretStore.FormatVersion, bytes[4]);
        Assert.DoesNotContain(key, Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
        Assert.DoesNotContain(key, Encoding.Unicode.GetString(bytes), StringComparison.Ordinal);

        // A new store (a restart) decrypts it for this Windows user.
        using var reopened = new DpapiSecretStore(new SecretStoreOptions(_m3.Directory.File("secrets.bin")), NullLogger<DpapiSecretStore>.Instance);
        Assert.True(reopened.HasKey("anthropic"));
        Assert.Equal(key, await reopened.GetKeyAsync("anthropic", CancellationToken.None));

        var cleared = await _m3.ResultAsync("ai.clearKey", new { provider = "anthropic" });
        Assert.Equal("""{"hasKey":false}""", cleared.GetRawText());
        Assert.Null(await new DpapiSecretStore(new SecretStoreOptions(_m3.Directory.File("secrets.bin")), NullLogger<DpapiSecretStore>.Instance).GetKeyAsync("anthropic", CancellationToken.None));
    }

    [Theory]
    [InlineData("gemini", "sk-test-0123456789", "Provider 'gemini'")]
    [InlineData("openai", "short", "does not look like an API key")]
    [InlineData("openai", "sk test with spaces", "does not look like an API key")]
    public async Task BadKeysAreRefusedWithoutEchoingThem(string provider, string key, string expected)
    {
        var error = await _m3.ErrorAsync("ai.setKey", new { provider, key });

        Assert.Equal(BridgeErrorCodes.InvalidParams, error.GetProperty("code").GetString());
        Assert.Contains(expected, error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain(key, error.GetRawText(), StringComparison.Ordinal);
        Assert.False(File.Exists(_m3.Directory.File("secrets.bin")));
    }

    [Fact]
    public async Task ADamagedSecretsFileReadsAsNoKeys()
    {
        File.WriteAllBytes(_m3.Directory.File("secrets.bin"), [.. "MSEC"u8.ToArray(), DpapiSecretStore.FormatVersion, 1, 2, 3]);
        using var store = new DpapiSecretStore(new SecretStoreOptions(_m3.Directory.File("secrets.bin")), NullLogger<DpapiSecretStore>.Instance);

        Assert.False(store.HasKey("openai"));
        await store.SetKeyAsync("openai", "sk-replacement-key", CancellationToken.None);
        Assert.True(store.HasKey("openai"));
    }

    [Fact]
    public async Task SetStartupChangesTheEntryAndTheSetting()
    {
        var on = await _m3.ResultAsync("app.setStartup", new { startWithWindows = true });

        Assert.Equal("""{"startWithWindows":true}""", on.GetRawText());
        Assert.True(_m3.Startup.IsEnabled);
        Assert.True(_m3.Host.Settings.Current.General.StartWithWindows);

        // settings.set general.startWithWindows does the same.
        var settings = await _m3.ResultAsync("settings.set", new { general = new { startWithWindows = false } });
        Assert.False(_m3.Startup.IsEnabled);
        Assert.False(settings.GetProperty("general").GetProperty("startWithWindows").GetBoolean());
    }

    [Fact]
    public async Task ARefusedStartupChangeChangesNothing()
    {
        _m3.Startup.Refuse = true;

        var error = await _m3.ErrorAsync("app.setStartup", new { startWithWindows = true });
        var viaSettings = await _m3.ErrorAsync("settings.set", new { general = new { startWithWindows = true } });

        Assert.Equal(BridgeErrorCodes.Internal, error.GetProperty("code").GetString());
        Assert.Contains("Nothing was changed", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal(BridgeErrorCodes.Internal, viaSettings.GetProperty("code").GetString());
        Assert.False(_m3.Host.Settings.Current.General.StartWithWindows);
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void TheRegistryEntryIsAddedAndRemoved()
    {
        var keyPath = @"Software\Memento.Tests." + Guid.NewGuid().ToString("N");
        var registration = new RegistryStartupRegistration(keyPath, "Memento", () => @"C:\Program Files\Memento\Memento.exe");
        try
        {
            registration.SetEnabled(true);
            Assert.True(registration.IsEnabled);
            using (var key = Registry.CurrentUser.OpenSubKey(keyPath))
            {
                Assert.Equal("\"C:\\Program Files\\Memento\\Memento.exe\"", key!.GetValue("Memento"));
            }

            registration.SetEnabled(false);
            Assert.False(registration.IsEnabled);
            registration.SetEnabled(false);
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false);
        }
    }
}
