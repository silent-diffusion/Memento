using System.Security.Cryptography;
using Memento.Core.Bridge;
using Memento.Core.Models;
using Memento.Core.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Memento.Core.Tests.Models;

/// <summary>Install, resume, verify, cancel and remove against a local HTTP server.</summary>
public sealed class ModelManagerTests : IDisposable
{
    private readonly TempDirectory _directory = new();
    private readonly LocalHttpServer _server = new();
    private readonly RecordingEventSink _sink = new();
    private readonly FakeFreeSpaceProbe _freeSpace = new();
    private readonly ModelDownloadClient _client = new();
    private readonly byte[] _content = RandomNumberGenerator.GetBytes(3 * 1024 * 1024 + 123);
    private ModelManager? _manager;

    public void Dispose()
    {
        _manager?.Dispose();
        _client.Dispose();
        _server.Dispose();
        _directory.Dispose();
    }

    private string ModelPath => Path.Combine(_directory.Path, "models", "whisper", "ggml-test.bin");

    private ModelManager Create(string? sha = null, long? size = null, string path = "ggml-test.bin")
    {
        _server.Add("ggml-test.bin", _content);
        var hash = sha ?? Convert.ToHexString(SHA256.HashData(_content)).ToLowerInvariant();
        var catalog = ModelCatalog.Parse($$"""
            { "schemaVersion": 1, "models": [ {
              "id": "test", "engine": "whisper", "kind": "transcription", "name": "Test model", "description": "d",
              "fileName": "ggml-test.bin", "sizeBytes": {{size ?? _content.Length}}, "sha256": "{{hash}}",
              "url": "{{_server.Url(path)}}", "license": "MIT", "runsOn": "either", "accuracyNote": "n" } ] }
            """);
        _manager = new ModelManager(
            catalog,
            new ModelStoreOptions(Path.Combine(_directory.Path, "models")) { SpaceMarginBytes = 0, ConnectTimeout = TimeSpan.FromSeconds(5) },
            _client,
            _freeSpace,
            new BridgeEventPublisher(_sink),
            NullLogger<ModelManager>.Instance);
        return _manager;
    }

    private Task<System.Text.Json.JsonElement> FinishedAsync(int timeoutMs = Patience.CeilingMs) =>
        _sink.WaitForAsync("models.progress", p => p.GetProperty("state").GetString() is "done" or "failed", timeoutMs);

    [Fact]
    public async Task InstallsVerifiesAndReportsProgress()
    {
        var manager = Create();
        var installed = new TaskCompletionSource<string>();
        manager.Installed += (_, id) => installed.TrySetResult(id);

        await manager.InstallAsync("test", CancellationToken.None);
        var last = await FinishedAsync();

        Assert.Equal("done", last.GetProperty("state").GetString());
        Assert.Equal(100, last.GetProperty("percent").GetInt32());
        Assert.Equal(_content.Length, last.GetProperty("bytesTotal").GetInt64());
        Assert.Equal("test", await installed.Task.WaitAsync(Patience.Ceiling));
        Assert.True(manager.IsInstalled("test"));
        Assert.Equal(ModelPath, manager.Resolve("test"));
        Assert.Equal(_content, await File.ReadAllBytesAsync(ModelPath));
        Assert.False(File.Exists(ModelPath + ".part"));
        var states = _sink.Payloads("models.progress").Select(p => p.GetProperty("state").GetString()).Distinct().ToList();
        Assert.Equal(["downloading", "verifying", "done"], states);
        Assert.True(Assert.Single(manager.List()).Installed);
    }

    [Fact]
    public async Task AChecksumMismatchInstallsNothingAndRemovesTheFile()
    {
        var manager = Create(sha: new string('0', 64));

        await manager.InstallAsync("test", CancellationToken.None);
        var last = await FinishedAsync();

        Assert.Equal("failed", last.GetProperty("state").GetString());
        Assert.Contains("did not match its published checksum", last.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.False(manager.IsInstalled("test"));
        Assert.False(File.Exists(ModelPath));
        Assert.False(File.Exists(ModelPath + ".part"));
    }

    [Fact]
    public async Task ResumesAPartialDownloadWithARangeRequest()
    {
        var manager = Create();
        Directory.CreateDirectory(Path.GetDirectoryName(ModelPath)!);
        await File.WriteAllBytesAsync(ModelPath + ".part", _content[..1_000_000]);

        await manager.InstallAsync("test", CancellationToken.None);
        var last = await FinishedAsync();

        Assert.Equal("done", last.GetProperty("state").GetString());
        Assert.Contains(_server.Requests, r => r.EndsWith("range 1000000-", StringComparison.Ordinal));
        Assert.Equal(_content, await File.ReadAllBytesAsync(ModelPath));
    }

    [Fact]
    public async Task ADroppedConnectionKeepsThePartForTheNextAttempt()
    {
        var manager = Create();
        _server.CutAfterBytes = 1_500_000;

        await manager.InstallAsync("test", CancellationToken.None);
        var failed = await FinishedAsync();

        Assert.Equal("failed", failed.GetProperty("state").GetString());
        Assert.Contains("continues from there", failed.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal(1_500_000, new FileInfo(ModelPath + ".part").Length);

        _sink.Clear();
        _server.CutAfterBytes = null;
        await manager.InstallAsync("test", CancellationToken.None);
        Assert.Equal("done", (await FinishedAsync()).GetProperty("state").GetString());
        Assert.Equal(_content, await File.ReadAllBytesAsync(ModelPath));
    }

    [Fact]
    public async Task CancellingRemovesThePartialFile()
    {
        var manager = Create();
        _server.ChunkSize = 16 * 1024;
        _server.ChunkDelay = TimeSpan.FromMilliseconds(20);

        await manager.InstallAsync("test", CancellationToken.None);
        await TestRecordings.WaitUntilAsync(() => File.Exists(ModelPath + ".part") && new FileInfo(ModelPath + ".part").Length > 0, "download to start");
        await manager.CancelInstallAsync("test");
        var last = await FinishedAsync();

        Assert.Equal("failed", last.GetProperty("state").GetString());
        Assert.Contains("cancelled", last.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.False(File.Exists(ModelPath + ".part"));
        Assert.False(manager.IsInstalled("test"));
    }

    [Fact]
    public async Task ASecondDownloadWhileOneRunsIsBusyAndNamesTheRunningOne()
    {
        _server.Add("ggml-other.bin", new byte[10]);
        var catalog = ModelCatalog.Parse($$"""
            { "schemaVersion": 1, "models": [
              { "id": "test", "engine": "whisper", "kind": "transcription", "name": "Test model", "description": "d", "fileName": "ggml-test.bin", "sizeBytes": {{_content.Length}}, "sha256": "{{Convert.ToHexString(SHA256.HashData(_content)).ToLowerInvariant()}}", "url": "{{_server.Url("ggml-test.bin")}}", "license": "MIT", "runsOn": "either", "accuracyNote": "n" },
              { "id": "other", "engine": "whisper", "kind": "transcription", "name": "Other model", "description": "d", "fileName": "ggml-other.bin", "sizeBytes": 10, "sha256": "{{Convert.ToHexString(SHA256.HashData(new byte[10])).ToLowerInvariant()}}", "url": "{{_server.Url("ggml-other.bin")}}", "license": "MIT", "runsOn": "either", "accuracyNote": "n" } ] }
            """);
        _server.Add("ggml-test.bin", _content);
        _server.ChunkSize = 16 * 1024;
        _server.ChunkDelay = TimeSpan.FromMilliseconds(20);
        _manager = new ModelManager(catalog, new ModelStoreOptions(Path.Combine(_directory.Path, "models")) { SpaceMarginBytes = 0 }, _client, _freeSpace, new BridgeEventPublisher(_sink), NullLogger<ModelManager>.Instance);

        await _manager.InstallAsync("test", CancellationToken.None);
        var error = await Assert.ThrowsAsync<BridgeException>(() => _manager.InstallAsync("other", CancellationToken.None));
        await _manager.InstallAsync("test", CancellationToken.None);

        Assert.Equal("models.busy", error.Code);
        Assert.Equal("test", error.Detail);
        Assert.Contains("Test model is downloading now", error.Message, StringComparison.Ordinal);
        await _manager.CancelInstallAsync("test");
    }

    [Fact]
    public async Task NotEnoughSpaceIsRefusedBeforeDownloading()
    {
        var manager = Create();
        _freeSpace.FreeBytes = 1000;

        var error = await Assert.ThrowsAsync<BridgeException>(() => manager.InstallAsync("test", CancellationToken.None));

        Assert.Equal("models.noSpace", error.Code);
        Assert.Contains("Nothing was downloaded", error.Message, StringComparison.Ordinal);
        Assert.Empty(_server.Requests);
    }

    [Fact]
    public async Task AnUnreachableServerAnswersDownloadFailedWithTheCause()
    {
        var manager = Create(path: "missing.bin");

        var error = await Assert.ThrowsAsync<BridgeException>(() => manager.InstallAsync("test", CancellationToken.None));

        Assert.Equal("models.downloadFailed", error.Code);
        Assert.Contains("404", error.Detail, StringComparison.Ordinal);
        Assert.False(manager.IsInstalled("test"));
    }

    [Fact]
    public async Task AnUnknownModelIsNotFound()
    {
        var manager = Create();

        var error = await Assert.ThrowsAsync<BridgeException>(() => manager.InstallAsync("nope", CancellationToken.None));

        Assert.Equal("models.notFound", error.Code);
    }

    [Fact]
    public async Task RemovingAModelInUseIsRefusedAndAfterwardsDeletesIt()
    {
        var manager = Create();
        await manager.InstallAsync("test", CancellationToken.None);
        await FinishedAsync();

        using (manager.Use("test"))
        {
            var error = await Assert.ThrowsAsync<BridgeException>(() => manager.RemoveAsync("test", CancellationToken.None));
            Assert.Equal("models.inUse", error.Code);
            Assert.True(manager.IsInstalled("test"));
        }

        await manager.RemoveAsync("test", CancellationToken.None);

        Assert.False(manager.IsInstalled("test"));
        Assert.False(File.Exists(ModelPath));
    }

    [Fact]
    public async Task AFileOfTheWrongSizeDoesNotCountAsInstalled()
    {
        var manager = Create();
        Directory.CreateDirectory(Path.GetDirectoryName(ModelPath)!);
        await File.WriteAllBytesAsync(ModelPath, new byte[10]);

        Assert.False(manager.IsInstalled("test"));
        Assert.Null(manager.Resolve("test"));
    }
}
