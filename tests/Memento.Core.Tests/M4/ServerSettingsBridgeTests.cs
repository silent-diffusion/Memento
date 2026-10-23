using System.Text.Json;
using Memento.Core.Bridge;
using Memento.Core.Tests.M3;

namespace Memento.Core.Tests.M4;

/// <summary>
/// "Your own server" in <c>settings.get</c> / <c>settings.set</c> (2.5.0, BRIDGE.md "2.5.0 server contract") and its
/// optional key through <c>ai.setKey</c> / <c>ai.clearKey</c>. Nothing here contacts the address.
/// </summary>
public sealed class ServerSettingsBridgeTests : IDisposable
{
    private readonly M3Host _host = new();

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task TheSnapshotHasNoServerUntilOneIsSet()
    {
        var settings = await _host.ResultAsync("settings.get", new { });

        Assert.Equal(
            """{"hasKey":false,"address":null,"model":null,"contextTokens":8192,"loopback":false,"leavesPc":false,"insecure":false}""",
            settings.GetProperty("ai").GetProperty("providers").GetProperty("server").GetRawText());
    }

    [Theory]
    [InlineData("localhost:11434/v1", "http://localhost:11434", true, false, false)]
    [InlineData("http://192.168.1.20:11434", "http://192.168.1.20:11434", false, true, false)]
    [InlineData("https://llm.example.com", "https://llm.example.com", false, true, false)]
    [InlineData("http://203.0.113.10:8000", "http://203.0.113.10:8000", false, true, true)]
    public async Task AnAddressIsSavedNormalizedWithWhereItRuns(string typed, string saved, bool loopback, bool leavesPc, bool insecure)
    {
        var set = await _host.Host.ResultAsync("settings.set", JsonSerializer.Serialize(new { ai = new { providers = new { server = new { address = typed, model = " qwen3:8b ", contextTokens = 32768 } } } }));

        var server = set.GetProperty("ai").GetProperty("providers").GetProperty("server");
        Assert.Equal(saved, server.GetProperty("address").GetString());
        Assert.Equal("qwen3:8b", server.GetProperty("model").GetString());
        Assert.Equal(32768, server.GetProperty("contextTokens").GetInt32());
        Assert.Equal((loopback, leavesPc, insecure), (server.GetProperty("loopback").GetBoolean(), server.GetProperty("leavesPc").GetBoolean(), server.GetProperty("insecure").GetBoolean()));
        Assert.Equal(saved, _host.Host.Settings.Current.Ai.Server.Address);
    }

    [Fact]
    public async Task ANullAddressOrModelClearsItAndAMissingFieldKeepsIt()
    {
        await _host.Host.ResultAsync("settings.set", """{"ai":{"providers":{"server":{"address":"http://localhost:1234","model":"qwen/qwen3-8b"}}}}""");

        // A JSON null scalar is a value here (clear it), not a null object the bridge refuses.
        var cleared = await _host.Host.ResultAsync("settings.set", """{"ai":{"providers":{"server":{"model":null}}}}""");
        Assert.Equal("http://localhost:1234", cleared.GetProperty("ai").GetProperty("providers").GetProperty("server").GetProperty("address").GetString());
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("ai").GetProperty("providers").GetProperty("server").GetProperty("model").ValueKind);

        var none = await _host.Host.ResultAsync("settings.set", """{"ai":{"providers":{"server":{"address":null}}}}""");
        Assert.Equal(JsonValueKind.Null, none.GetProperty("ai").GetProperty("providers").GetProperty("server").GetProperty("address").ValueKind);
        Assert.Null(_host.Host.Settings.Current.Ai.Server.Address);

        // A server block sent as null is no change (as for the other providers), and so is an empty one.
        await _host.Host.ResultAsync("settings.set", """{"ai":{"providers":{"server":{"address":"http://localhost:1234"}}}}""");
        await _host.Host.ResultAsync("settings.set", """{"ai":{"providers":{"server":null}}}""");
        await _host.Host.ResultAsync("settings.set", """{"ai":{"providers":{"server":{}}}}""");
        Assert.Equal("http://localhost:1234", _host.Host.Settings.Current.Ai.Server.Address);
    }

    [Theory]
    [InlineData("""{"address":"ftp://localhost"}""", "ai.providers.server.address")]
    [InlineData("""{"address":""}""", "ai.providers.server.address")]
    [InlineData("""{"address":"http://user:hunter2-pass@192.168.1.2:8000"}""", "ai.providers.server.address")]
    [InlineData("""{"address":42}""", "ai.providers.server.address")]
    [InlineData("""{"model":"has space"}""", "ai.providers.server.model")]
    [InlineData("""{"model":""}""", "ai.providers.server.model")]
    public async Task ABadValueNamesItsFieldAndChangesNothing(string server, string field)
    {
        var response = await _host.Host.CallAsync("settings.set", """{"theme":"dark","ai":{"providers":{"server":""" + server + "}}}");

        var error = response.GetProperty("error");
        Assert.Equal(DomainErrorCodes.SettingsInvalidValue, error.GetProperty("code").GetString());
        Assert.Equal(field, error.GetProperty("detail").GetString());
        Assert.DoesNotContain("hunter2", error.GetRawText(), StringComparison.Ordinal);
        Assert.Equal("system", _host.Host.Settings.Current.Theme);
        Assert.Null(_host.Host.Settings.Current.Ai.Server.Address);
    }

    [Theory]
    [InlineData(512, 2048)]
    [InlineData(4096, 4096)]
    [InlineData(3_000_000, 1_048_576)]
    public async Task TheContextIsClampedAsThePageClampsIt(int sent, int saved)
    {
        var set = await _host.Host.ResultAsync("settings.set", JsonSerializer.Serialize(new { ai = new { providers = new { server = new { contextTokens = sent } } } }));

        Assert.Equal(saved, set.GetProperty("ai").GetProperty("providers").GetProperty("server").GetProperty("contextTokens").GetInt32());
        Assert.Equal(saved, _host.Host.Settings.Current.Ai.Server.ContextTokens);
    }

    [Fact]
    public async Task TheServerCanBeTheDefaultProvider()
    {
        var set = await _host.Host.ResultAsync("settings.set", """{"ai":{"defaultProviderId":"server"}}""");

        Assert.Equal("server", set.GetProperty("ai").GetProperty("defaultProviderId").GetString());
    }

    [Fact]
    public async Task TheKeyIsStoredLikeTheCloudKeysAndMayBeShort()
    {
        var set = await _host.ResultAsync("ai.setKey", new { provider = "server", key = "tok123" });
        var settings = await _host.ResultAsync("settings.get", new { });

        Assert.Equal("""{"hasKey":true}""", set.GetRawText());
        Assert.True(settings.GetProperty("ai").GetProperty("providers").GetProperty("server").GetProperty("hasKey").GetBoolean());
        Assert.False(settings.GetProperty("ai").GetProperty("providers").GetProperty("anthropic").GetProperty("hasKey").GetBoolean());
        Assert.DoesNotContain("tok123", settings.GetRawText(), StringComparison.Ordinal);

        var cleared = await _host.ResultAsync("ai.clearKey", new { provider = "server" });
        Assert.Equal("""{"hasKey":false}""", cleared.GetRawText());

        // A key never goes through settings.set.
        var refused = await _host.Host.CallAsync("settings.set", """{"ai":{"providers":{"server":{"hasKey":true}}}}""");
        Assert.Equal(BridgeErrorCodes.InvalidParams, refused.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task AnUnknownProviderListsTheThreeThatTakeAKey()
    {
        var response = await _host.Host.CallAsync("ai.setKey", """{"provider":"gemini","key":"sk-test-0123456789"}""");

        Assert.Contains("Choose anthropic, openai or server.", response.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
    }
}
