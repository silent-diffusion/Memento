using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Memento.Core.Bridge;
using Memento.Core.Models;
using Memento.Core.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Memento.Core.Tests;

/// <summary>
/// Memento makes HTTP requests in exactly two places, so what leaves the PC can be reviewed there: the model download
/// client and the cloud AI client. This scans the source tree so a new client elsewhere fails the build's tests.
/// </summary>
public sealed partial class NetworkSurfaceTests
{
    private static readonly string[] HttpClientFiles =
    [
        Path.Combine("src", "Memento.AI", "Http", "AiHttpClient.cs"),
        Path.Combine("src", "Memento.Core", "Models", "ModelDownloadClient.cs"),
    ];

    [Fact]
    public void HttpClientsAreCreatedOnlyInTheTwoNetworkClients()
    {
        var root = RepoRoot();
        var offenders = new List<string>();
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in SourceFiles(root))
        {
            var relative = Path.GetRelativePath(root, file);
            var text = File.ReadAllText(file);
            if (HttpClientConstruction().IsMatch(text) || text.Contains("SocketsHttpHandler", StringComparison.Ordinal))
            {
                if (HttpClientFiles.Contains(relative, StringComparer.OrdinalIgnoreCase))
                {
                    found.Add(relative);
                }
                else
                {
                    offenders.Add(relative);
                }
            }

            if (OtherNetworkApi().Match(text) is { Success: true } other)
            {
                offenders.Add($"{relative} ({other.Value})");
            }
        }

        Assert.Empty(offenders);
        Assert.Equal(HttpClientFiles.Order(StringComparer.OrdinalIgnoreCase), found.Order(StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task TheModelDownloadClientSendsThroughTheHandlerItIsGiven()
    {
        using var directory = new TempDirectory();
        var handler = new RecordingHttpHandler();
        using var client = new ModelDownloadClient(handler);
        var sha = Convert.ToHexString(SHA256.HashData(new byte[4])).ToLowerInvariant();
        var catalog = ModelCatalog.Parse($$"""
            { "schemaVersion": 1, "models": [ {
              "id": "test", "engine": "whisper", "kind": "transcription", "name": "Test model", "description": "d",
              "fileName": "ggml-test.bin", "sizeBytes": 4, "sha256": "{{sha}}",
              "url": "https://huggingface.co/memento-test/ggml-test.bin", "license": "MIT", "runsOn": "either", "accuracyNote": "n" } ] }
            """);
        using var manager = new ModelManager(
            catalog,
            new ModelStoreOptions(directory.File("models")) { SpaceMarginBytes = 0 },
            client,
            new FakeFreeSpaceProbe(),
            new BridgeEventPublisher(new RecordingEventSink()),
            NullLogger<ModelManager>.Instance);

        var error = await Assert.ThrowsAsync<BridgeException>(() => manager.InstallAsync("test", CancellationToken.None));

        Assert.Equal("models.downloadFailed", error.Code);
        Assert.Equal([new Uri("https://huggingface.co/memento-test/ggml-test.bin")], handler.Requests);
    }

    private static IEnumerable<string> SourceFiles(string root) =>
        Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"));

    private static string RepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Memento.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"Memento.sln was not found above {AppContext.BaseDirectory}.");
    }

    // "new HttpClient(" and the target-typed "HttpClient name = new(".
    [GeneratedRegex(@"new\s+(System\.Net\.Http\.)?HttpClient\s*\(|\bHttpClient\??\s+\w+\s*=\s*new\s*\(", RegexOptions.CultureInvariant)]
    private static partial Regex HttpClientConstruction();

    [GeneratedRegex(@"\bHttpClientHandler\b|\bWebRequest\.Create|new\s+WebClient\s*\(|new\s+TcpClient\s*\(|new\s+ClientWebSocket\s*\(|new\s+UdpClient\s*\(", RegexOptions.CultureInvariant)]
    private static partial Regex OtherNetworkApi();
}
