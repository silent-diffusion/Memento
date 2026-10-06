using System.Text.Json;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Tests.Fakes;

namespace Memento.Core.Tests.Bridge;

/// <summary>
/// Pins the exact JSON of every M0 method and event. If one of these fails, update
/// <c>ui/src/bridge/types.ts</c> in the same change.
/// </summary>
public sealed class ContractSerializationTests : IDisposable
{
    private readonly BridgeTestHost _host = new();

    public void Dispose() => _host.Dispose();

    private async Task<string> CallAsync(string method, string parameters = "{}") =>
        await _host.Router.HandleAsync($$"""{"id":1,"method":"{{method}}","params":{{parameters}}}""", CancellationToken.None);

    [Fact]
    public async Task AppVersion()
    {
        _host.Theme.IsDark = true;

        var json = await CallAsync("app.version");

        Assert.Equal("""{"id":1,"result":{"version":"0.1.0","osVersion":"Windows 10.0.26200","isDarkTheme":true}}""", json);
    }

    [Fact]
    public async Task SettingsGetReturnsDefaultsWithTheEffectiveLibraryPath()
    {
        var json = await CallAsync("settings.get");

        var expected = JsonSerializer.Serialize(
            new BridgeResponse(
                1,
                JsonSerializer.SerializeToElement(
                    new SettingsSnapshot("system", AppPaths.DefaultLibrary, "comfortable"),
                    BridgeJsonContext.Default.SettingsSnapshot),
                null),
            BridgeJsonContext.Default.BridgeResponse);
        Assert.Equal(expected, json);
        Assert.Contains("\"theme\":\"system\"", json, StringComparison.Ordinal);
        Assert.Contains("\"listDensity\":\"comfortable\"", json, StringComparison.Ordinal);
        Assert.Contains("\"libraryPath\":", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SettingsSetReturnsTheUpdatedSnapshot()
    {
        var json = await CallAsync("settings.set", """{"theme":"dark"}""");

        using var document = JsonDocument.Parse(json);
        var result = document.RootElement.GetProperty("result");
        Assert.Equal(["theme", "libraryPath", "listDensity"], result.EnumerateObject().Select(p => p.Name));
        Assert.Equal("dark", result.GetProperty("theme").GetString());
    }

    [Fact]
    public async Task LibraryListIsEmptyWithTheDocumentedShape()
    {
        var json = await CallAsync("library.list");

        Assert.Equal("""{"id":1,"result":{"recordings":[],"totalDurationMs":0}}""", json);
    }

    [Fact]
    public async Task OpenExternal()
    {
        var json = await CallAsync("app.openExternal", """{"url":"https://github.com/silent-diffusion/Memento"}""");

        Assert.Equal("""{"id":1,"result":{"opened":true}}""", json);
    }

    [Fact]
    public async Task UiReady()
    {
        var json = await CallAsync("ui.ready");

        Assert.Equal("""{"id":1,"result":{}}""", json);
    }

    [Fact]
    public async Task ErrorResponse()
    {
        var json = await CallAsync("settings.set", """{"theme":"purple"}""");

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal(["id", "error"], root.EnumerateObject().Select(p => p.Name));
        Assert.Equal(1, root.GetProperty("id").GetInt64());
        var error = root.GetProperty("error");
        Assert.Equal(["code", "message", "detail"], error.EnumerateObject().Select(p => p.Name));
        Assert.Equal("settings.invalidValue", error.GetProperty("code").GetString());
        Assert.Equal("Theme 'purple' is not available. Choose system, light, dark.", error.GetProperty("message").GetString());
        Assert.Equal(JsonValueKind.Null, error.GetProperty("detail").ValueKind);
    }

    [Fact]
    public void RecordingSummaryShape()
    {
        var summary = new RecordingSummary(
            "20261006-100000-k3f9ab",
            "Q3 planning sync",
            "meeting",
            new DateTimeOffset(2026, 10, 6, 10, 0, 0, TimeSpan.FromHours(1)),
            3_734_000,
            5,
            HasVideo: false,
            [new StageStatus("transcript", "active", 64), new StageStatus("speakers", "queued", null)]);

        var json = JsonSerializer.Serialize(
            new LibraryListResult([summary], summary.DurationMs),
            BridgeJsonContext.Default.LibraryListResult);

        Assert.Equal(
            """{"recordings":[{"id":"20261006-100000-k3f9ab","title":"Q3 planning sync","type":"meeting","createdAt":"2026-10-06T10:00:00+01:00","durationMs":3734000,"participantCount":5,"hasVideo":false,"stages":[{"stage":"transcript","state":"active","percent":64},{"stage":"speakers","state":"queued","percent":null}]}],"totalDurationMs":3734000}""",
            json);
    }

    [Fact]
    public void ThemeChangedEvent()
    {
        Assert.Equal(
            """{"event":"theme.changed","payload":{"isDark":true}}""",
            BridgeEventPublisher.SerializeThemeChanged(new ThemeChangedPayload(true)));
    }

    [Fact]
    public void FooterStatusEvent()
    {
        Assert.Equal(
            """{"event":"status.footer","payload":{"engine":{"ready":false,"device":null},"storage":{"freeBytes":227633266688,"lowSpace":false}}}""",
            BridgeEventPublisher.SerializeFooterStatus(
                new FooterStatusPayload(new EngineStatus(false, null), new StorageStatus(227_633_266_688, false))));
    }

    [Fact]
    public void RequestEnvelopeRoundTrips()
    {
        using var parameters = JsonDocument.Parse("""{"theme":"light"}""");
        var request = new BridgeRequest(17, "settings.set", parameters.RootElement.Clone());

        var json = JsonSerializer.Serialize(request, BridgeJsonContext.Default.BridgeRequest);
        var back = JsonSerializer.Deserialize(json, BridgeJsonContext.Default.BridgeRequest)!;

        Assert.Equal("""{"id":17,"method":"settings.set","params":{"theme":"light"}}""", json);
        Assert.Equal(17, back.Id);
        Assert.Equal("settings.set", back.Method);
        Assert.Equal("light", back.Params!.Value.GetProperty("theme").GetString());
    }

    [Theory]
    [InlineData("""{"theme":"light","listDensity":"compact"}""", "light", "compact")]
    [InlineData("""{"theme":null}""", null, null)]
    [InlineData("""{}""", null, null)]
    public void SettingsSetParamsAreAllOptional(string json, string? theme, string? density)
    {
        var parameters = JsonSerializer.Deserialize(json, BridgeJsonContext.Default.SettingsSetParams)!;

        Assert.Equal(theme, parameters.Theme);
        Assert.Equal(density, parameters.ListDensity);
        Assert.Null(parameters.LibraryPath);
    }

    [Fact]
    public void UnknownParamFieldsAreRejected()
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize("""{"theme":"light","colour":"red"}""", BridgeJsonContext.Default.SettingsSetParams));
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize("""{"anything":1}""", BridgeJsonContext.Default.EmptyParams));
    }

    [Fact]
    public void OpenExternalRequiresUrl()
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize("{}", BridgeJsonContext.Default.OpenExternalParams));
    }
}
