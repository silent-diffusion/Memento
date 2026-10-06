using System.Text.Json;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Methods;
using Memento.Core.Tests.Fakes;

namespace Memento.Core.Tests.Bridge;

public sealed class BridgeMethodTests : IDisposable
{
    private readonly BridgeTestHost _host = new();

    public void Dispose() => _host.Dispose();

    private Task<JsonElement> CallAsync(string method, string parameters = "{}") =>
        _host.SendAsync($$"""{"id":3,"method":"{{method}}","params":{{parameters}}}""");

    private static string? ErrorCode(JsonElement response) =>
        response.TryGetProperty("error", out var error) ? error.GetProperty("code").GetString() : null;

    [Fact]
    public async Task AppVersionReportsTheEffectiveTheme()
    {
        _host.Theme.IsDark = false;
        var light = await CallAsync("app.version");
        _host.Theme.IsDark = true;
        var dark = await CallAsync("app.version");

        Assert.False(light.GetProperty("result").GetProperty("isDarkTheme").GetBoolean());
        Assert.True(dark.GetProperty("result").GetProperty("isDarkTheme").GetBoolean());
    }

    [Fact]
    public async Task MethodsWithoutParamsRejectUnexpectedFields()
    {
        var response = await CallAsync("library.list", """{"filter":"meeting"}""");

        Assert.Equal(BridgeErrorCodes.InvalidParams, ErrorCode(response));
        Assert.Contains("filter", response.GetProperty("error").GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SettingsSetIsAPartialUpdateThatPersists()
    {
        await CallAsync("settings.set", """{"listDensity":"compact"}""");
        var response = await CallAsync("settings.set", """{"theme":"light"}""");

        var result = response.GetProperty("result");
        Assert.Equal("light", result.GetProperty("theme").GetString());
        Assert.Equal("compact", result.GetProperty("listDensity").GetString());

        var onDisk = await File.ReadAllTextAsync(_host.SettingsFile);
        Assert.Contains("\"theme\": \"light\"", onDisk, StringComparison.Ordinal);
        Assert.Contains("\"listDensity\": \"compact\"", onDisk, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"theme":"purple"}""")]
    [InlineData("""{"listDensity":"tiny"}""")]
    [InlineData("""{"theme":"dark","listDensity":"tiny"}""")]
    public async Task SettingsSetRejectsInvalidValuesAndChangesNothing(string parameters)
    {
        var response = await CallAsync("settings.set", parameters);

        Assert.Equal(SettingsSetMethod.InvalidValueCode, ErrorCode(response));
        Assert.Equal("system", _host.Settings.Current.Theme);
        Assert.False(File.Exists(_host.SettingsFile));
    }

    [Fact]
    public async Task SettingsSetRejectsWrongTypes()
    {
        var response = await CallAsync("settings.set", """{"theme":3}""");

        Assert.Equal(BridgeErrorCodes.InvalidParams, ErrorCode(response));
    }

    [Fact]
    public async Task SettingsSetRefusesToMoveTheLibrary()
    {
        var response = await CallAsync("settings.set", """{"libraryPath":"D:\\Elsewhere"}""");

        Assert.Equal(SettingsSetMethod.LibraryMoveUnavailableCode, ErrorCode(response));
        Assert.Null(_host.Settings.Current.LibraryPath);
    }

    [Fact]
    public async Task SettingsSetAcceptsTheCurrentLibraryPathUnchanged()
    {
        var current = JsonSerializer.Serialize(AppPaths.DefaultLibrary);

        var response = await CallAsync("settings.set", $$"""{"libraryPath":{{current}},"theme":"dark"}""");

        Assert.Null(ErrorCode(response));
        Assert.Equal("dark", _host.Settings.Current.Theme);
    }

    [Fact]
    public async Task SettingsSetRaisesChangedForTheThemeService()
    {
        var raised = 0;
        _host.Settings.Changed += (_, args) =>
        {
            raised++;
            Assert.Equal("system", args.Previous.Theme);
            Assert.Equal("dark", args.Current.Theme);
        };

        await CallAsync("settings.set", """{"theme":"dark"}""");

        Assert.Equal(1, raised);
    }

    [Theory]
    [InlineData("https://github.com/silent-diffusion/Memento/releases")]
    [InlineData("ms-settings:privacy-microphone")]
    [InlineData("ms-settings:sound")]
    public async Task OpenExternalOpensHttpsAndWindowsSettings(string url)
    {
        var response = await CallAsync("app.openExternal", JsonSerializer.Serialize(new { url }));

        Assert.True(response.GetProperty("result").GetProperty("opened").GetBoolean());
        Assert.Equal(new Uri(url), Assert.Single(_host.Launcher.Opened));
    }

    [Theory]
    [InlineData("http://example.com")]
    [InlineData("file:///C:/Windows/System32/cmd.exe")]
    [InlineData("C:\\Windows\\System32\\cmd.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ms-word:ofe|u|https://example.com/a.docx")]
    [InlineData("https://user:pass@example.com")]
    [InlineData("relative/path")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task OpenExternalRefusesEverythingElse(string url)
    {
        var response = await CallAsync("app.openExternal", JsonSerializer.Serialize(new { url }));

        Assert.Equal(OpenExternalMethod.UnsupportedTargetCode, ErrorCode(response));
        Assert.Empty(_host.Launcher.Opened);
    }

    [Fact]
    public async Task OpenExternalRefusesOverlongUrls()
    {
        var url = "https://example.com/" + new string('a', 2100);

        var response = await CallAsync("app.openExternal", JsonSerializer.Serialize(new { url }));

        Assert.Equal(OpenExternalMethod.UnsupportedTargetCode, ErrorCode(response));
    }

    [Fact]
    public async Task OpenExternalReportsWhenWindowsCannotOpenTheTarget()
    {
        _host.Launcher.Succeeds = false;

        var response = await CallAsync("app.openExternal", """{"url":"https://example.com/help"}""");

        Assert.Equal(OpenExternalMethod.LaunchFailedCode, ErrorCode(response));
        Assert.Contains("example.com", response.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UiReadyNotifiesTheHost()
    {
        await CallAsync("ui.ready");

        Assert.Equal(1, _host.Lifecycle.ReadyCount);
    }
}
