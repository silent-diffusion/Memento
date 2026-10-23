using System.Text.Json;
using Memento.Core.Ai;
using Memento.Core.Settings;
using Memento.Core.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Memento.Core.Tests.Settings;

/// <summary>
/// "Your own server" (2.5.0): how an address is read and where it is judged to run, and how a settings file's server
/// block is normalized on load (a hand-edited file may hold anything).
/// </summary>
public sealed class ServerSettingsTests
{
    private static readonly string[] Hosts = ["localhost", "192.168.0.2", "203.0.113.1"];

    [Theory]
    [InlineData("http://localhost:11434", "http://localhost:11434", ServerZone.ThisPc, false)]
    [InlineData("localhost:1234", "http://localhost:1234", ServerZone.ThisPc, false)]
    [InlineData("  http://127.0.0.1:8080/v1/  ", "http://127.0.0.1:8080", ServerZone.ThisPc, false)]
    [InlineData("http://127.5.6.7:8000", "http://127.5.6.7:8000", ServerZone.ThisPc, false)]
    [InlineData("http://[::1]:11434", "http://[::1]:11434", ServerZone.ThisPc, false)]
    [InlineData("HTTP://LOCALHOST:11434/V1", "http://localhost:11434", ServerZone.ThisPc, false)]
    [InlineData("http://192.168.1.20:11434", "http://192.168.1.20:11434", ServerZone.Network, false)]
    [InlineData("http://10.0.0.5:8000", "http://10.0.0.5:8000", ServerZone.Network, false)]
    [InlineData("http://172.16.4.2:8000", "http://172.16.4.2:8000", ServerZone.Network, false)]
    [InlineData("http://172.31.255.1:8000", "http://172.31.255.1:8000", ServerZone.Network, false)]
    [InlineData("http://169.254.10.10:8000", "http://169.254.10.10:8000", ServerZone.Network, false)]
    [InlineData("http://[fe80::1]:8000", "http://[fe80::1]:8000", ServerZone.Network, false)]
    [InlineData("http://[fd12:3456::1]:8000", "http://[fd12:3456::1]:8000", ServerZone.Network, false)]
    [InlineData("http://gpu-box:11434", "http://gpu-box:11434", ServerZone.Network, false)]
    [InlineData("http://gpu-box.local:11434", "http://gpu-box.local:11434", ServerZone.Network, false)]
    [InlineData("https://llm.example.com/ollama", "https://llm.example.com/ollama", ServerZone.Remote, false)]
    [InlineData("http://llm.example.com:8000", "http://llm.example.com:8000", ServerZone.Remote, true)]
    [InlineData("http://172.32.0.1:8000", "http://172.32.0.1:8000", ServerZone.Remote, true)]
    [InlineData("http://203.0.113.10:8000/v1", "http://203.0.113.10:8000", ServerZone.Remote, true)]
    public void AnAddressIsNormalizedAndJudgedFromItsTextAlone(string text, string saved, ServerZone zone, bool insecure)
    {
        var address = ServerAddress.Parse(text, out var problem);

        Assert.Null(problem);
        Assert.NotNull(address);
        Assert.Equal(saved, address.Text);
        Assert.Equal(zone, address.Zone);
        Assert.Equal(insecure, address.IsInsecure);
        Assert.Equal(zone != ServerZone.ThisPc, address.LeavesPc);
        Assert.Equal(saved + "/v1/chat/completions", address.Resolve("v1/chat/completions").AbsoluteUri);
    }

    [Theory]
    [InlineData("", "it is empty")]
    [InlineData("ftp://localhost:21", "it is not an http:// or https:// address")]
    [InlineData("http://user:secret@localhost:11434", "user name or password")]
    [InlineData("http://localhost:11434/?key=1", "query or a fragment")]
    [InlineData("http://localhost:11434/#top", "query or a fragment")]
    [InlineData("http://", "not an http")]
    public void AnAddressThatCannotBeUsedSaysWhy(string text, string reason)
    {
        Assert.Null(ServerAddress.Parse(text, out var problem));
        Assert.Contains(reason, problem, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLabelsNameTheHostAndWhereItRuns()
    {
        Assert.Equal("localhost:11434", ServerAddress.Parse("http://localhost:11434", out _)!.HostAndPort);
        Assert.Equal("llm.example.com", ServerAddress.Parse("https://llm.example.com", out _)!.HostAndPort);
        Assert.Equal(["this PC", "on your network", "remote"], Hosts.Select(h => ServerAddress.Parse("https://" + h, out _)!.ZoneLabel));
    }

    [Theory]
    [InlineData("qwen3:8b", true)]
    [InlineData("hf.co/org/Model-GGUF:Q4_K_M", true)]
    [InlineData("qwen/qwen3-8b@q4_k_m", true)]
    [InlineData("Qwen/Qwen3-8B", true)]
    [InlineData("", false)]
    [InlineData("has space", false)]
    [InlineData("-dash", false)]
    [InlineData("<script>", false)]
    public void AModelIsWhatServersCallTheirs(string model, bool valid) => Assert.Equal(valid, AiServerSettings.IsValidModel(model));

    [Fact]
    public async Task AServerBlockIsNormalizedOnLoad()
    {
        using var directory = new TempDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        await File.WriteAllTextAsync(path, """
            { "schemaVersion": 2, "ai": { "server": { "address": "  LOCALHOST:11434/v1 ", "model": " qwen3:8b ", "contextTokens": 50, "futureField": 1 } } }
            """);
        var store = new JsonSettingsStore(path, NullLogger<JsonSettingsStore>.Instance);

        var server = (await store.LoadAsync(CancellationToken.None)).Ai.Server;

        Assert.Equal("http://localhost:11434", server.Address);
        Assert.Equal("qwen3:8b", server.Model);
        Assert.Equal(AiServerSettings.MinContextTokens, server.ContextTokens);
        Assert.True(server.ExtensionData!.ContainsKey("futureField"));
    }

    [Theory]
    [InlineData("""{ "address": "   " }""", null)]
    [InlineData("""{ "address": "http://user:secret@192.168.1.2:8000" }""", null)]
    [InlineData("""{ "address": "gopher://localhost" }""", null)]
    [InlineData("""{ "address": "http://localhost:11434", "contextTokens": 9000000 }""", "http://localhost:11434")]
    public async Task AnAddressThatDoesNotParseReadsAsNoneAndIsLoggedWithoutItsText(string block, string? expected)
    {
        using var directory = new TempDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        await File.WriteAllTextAsync(path, """{ "schemaVersion": 2, "ai": { "server": """ + block + " } }");
        var log = new CapturingLogger();
        var store = new JsonSettingsStore(path, log);

        var server = (await store.LoadAsync(CancellationToken.None)).Ai.Server;

        Assert.Equal(expected, server.Address);
        Assert.InRange(server.ContextTokens, AiServerSettings.MinContextTokens, AiServerSettings.MaxContextTokens);
        Assert.DoesNotContain("secret", string.Join("\n", log.Messages), StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingServerBlockReadsAsItsDefaults()
    {
        var settings = JsonSerializer.Deserialize("""{ "schemaVersion": 2, "ai": { "enabled": true } }""", SettingsJsonContext.Default.AppSettings)!;
        var empty = JsonSerializer.Deserialize("""{ "schemaVersion": 2, "ai": { "server": {} } }""", SettingsJsonContext.Default.AppSettings)!;

        Assert.Equal(new AiServerSettings(), settings.Ai.Server);
        Assert.Equal(new AiServerSettings(), empty.Ai.Server);
        Assert.Equal(8192, empty.Ai.Server.ContextTokens);
        Assert.Null(empty.Ai.Server.Address);
        Assert.Null(empty.Ai.Server.ParsedAddress());
    }

    private sealed class CapturingLogger : Microsoft.Extensions.Logging.ILogger<JsonSettingsStore>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
