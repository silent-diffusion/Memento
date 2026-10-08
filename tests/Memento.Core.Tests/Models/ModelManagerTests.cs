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
    public async Task AResumeAnsweredWithAnotherRangeStartsOverInsteadOfSplicing()
    {
        var manager = Create();
        Directory.CreateDirectory(Path.GetDirectoryName(ModelPath)!);
        await File.WriteAllBytesAsync(ModelPath + ".part", _content[..1_000_000]);
        _server.ContentRangeStart = 0; // Claims to send from byte 0 while the part ends at 1,000,000.

        await manager.InstallAsync("test", CancellationToken.None);
        var last = await FinishedAsync();

        Assert.Equal("done", last.GetProperty("state").GetString());
        var requests = _server.Requests;
        Assert.EndsWith("range 1000000-", requests[0], StringComparison.Ordinal);
        Assert.DoesNotContain("range", requests[1], StringComparison.Ordinal);
        Assert.Equal(_content, await File.ReadAllBytesAsync(ModelPath));
    }

    [Fact]
    public async Task AServerOfferingAnotherSizeFailsBeforeDownloading()
    {
        var manager = Create(size: _content.Length + 5);

        var error = await Assert.ThrowsAsync<BridgeException>(() => manager.InstallAsync("test", CancellationToken.None));

        Assert.Equal("models.downloadFailed", error.Code);
        Assert.Contains("but the published file is", error.Message, StringComparison.Ordinal);
        Assert.StartsWith("The download server offers", error.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(ModelPath + ".part"));
        Assert.False(manager.IsInstalled("test"));
    }

    [Fact]
    public async Task ADownloadLargerThanPublishedIsStoppedAndThePartRemoved()
    {
        var manager = Create(size: _content.Length - 1000);
        _server.OmitContentLength = true; // Only the bytes themselves show that it is too large.

        await manager.InstallAsync("test", CancellationToken.None);
        var last = await FinishedAsync();

        Assert.Equal("failed", last.GetProperty("state").GetString());
        Assert.Contains("larger than expected", last.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Contains("The partial file was removed", last.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.False(File.Exists(ModelPath + ".part"));
        Assert.False(manager.IsInstalled("test"));
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
    public async Task ARedirectOnTheSameServerIsFollowed()
    {
        _server.Redirects["moved"] = _server.Url("ggml-test.bin");
        var manager = Create(path: "moved");

        await manager.InstallAsync("test", CancellationToken.None);

        Assert.Equal("done", (await FinishedAsync()).GetProperty("state").GetString());
        Assert.True(manager.IsInstalled("test"));
    }

    [Fact]
    public async Task ARedirectToAnotherHostIsRefusedAndInstallsNothing()
    {
        _server.Redirects["moved"] = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"http://localhost:{_server.Port}/ggml-test.bin");
        var manager = Create(path: "moved");

        var error = await Assert.ThrowsAsync<BridgeException>(() => manager.InstallAsync("test", CancellationToken.None));

        Assert.Equal("models.downloadFailed", error.Code);
        Assert.Contains("sent it on to localhost", error.Message, StringComparison.Ordinal);
        Assert.Contains("not one of the servers Memento downloads models from", error.Message, StringComparison.Ordinal);
        Assert.False(manager.IsInstalled("test"));
        Assert.False(File.Exists(ModelPath));
        Assert.False(File.Exists(ModelPath + ".part"));
    }

    [Fact]
    public async Task AnInstalledModelIsStampedWithItsHash()
    {
        var manager = Create();

        await manager.InstallAsync("test", CancellationToken.None);
        await FinishedAsync();

        var stamp = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(ModelPath + ".verified.json")).RootElement;
        Assert.Equal(1, stamp.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(Convert.ToHexString(SHA256.HashData(_content)).ToLowerInvariant(), stamp.GetProperty("sha256").GetString());
        Assert.Equal(_content.Length, stamp.GetProperty("sizeBytes").GetInt64());
    }

    [Fact]
    public async Task AFlippedByteOfTheSameLengthIsNotInstalledAndIsSetAside()
    {
        var manager = Create();
        await manager.InstallAsync("test", CancellationToken.None);
        await FinishedAsync();
        _sink.Clear();

        var damaged = (byte[])_content.Clone();
        damaged[damaged.Length / 2] ^= 0x01;
        await File.WriteAllBytesAsync(ModelPath, damaged);

        Assert.False(manager.IsInstalled("test"));
        Assert.Null(manager.Resolve("test"));
        var failed = await FinishedAsync();
        Assert.Equal("failed", failed.GetProperty("state").GetString());
        Assert.Contains("does not match the published one", failed.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.False(File.Exists(ModelPath));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(ModelPath)!, "ggml-test.bin.corrupt-*"));
        Assert.False(manager.IsInstalled("test"));

        // Installing again downloads a good copy and clears the one set aside.
        _sink.Clear();
        await manager.InstallAsync("test", CancellationToken.None);
        Assert.Equal("done", (await FinishedAsync()).GetProperty("state").GetString());
        Assert.True(manager.IsInstalled("test"));
        Assert.Equal(_content, await File.ReadAllBytesAsync(ModelPath));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(ModelPath)!, "ggml-test.bin.corrupt-*"));
    }

    [Fact]
    public async Task AFlippedByteIsCaughtByAnExplicitVerify()
    {
        var manager = Create();
        await manager.InstallAsync("test", CancellationToken.None);
        await FinishedAsync();

        var damaged = (byte[])_content.Clone();
        damaged[0] ^= 0x80;
        await File.WriteAllBytesAsync(ModelPath, damaged);

        Assert.Equal(ModelCheck.Damaged, await manager.VerifyAsync("test", CancellationToken.None));
        Assert.False(manager.IsInstalled("test"));
        Assert.False(File.Exists(ModelPath + ".verified.json"));
    }

    [Fact]
    public async Task AModelFromAnOlderVersionIsHashedOnceInTheBackgroundAndThenCounts()
    {
        var manager = Create();
        var installed = new TaskCompletionSource<string>();
        manager.Installed += (_, id) => installed.TrySetResult(id);
        Directory.CreateDirectory(Path.GetDirectoryName(ModelPath)!);
        await File.WriteAllBytesAsync(ModelPath, _content);

        // The first look does not hash on the caller's thread: not installed yet.
        Assert.False(manager.IsInstalled("test"));

        Assert.Equal("test", await installed.Task.WaitAsync(TimeSpan.FromSeconds(20)));
        Assert.True(manager.IsInstalled("test"));
        Assert.Equal(ModelPath, manager.Resolve("test"));
        Assert.True(File.Exists(ModelPath + ".verified.json"));
        Assert.Empty(_server.Requests);
    }

    [Fact]
    public async Task InstallingAModelFromAnOlderVersionHashesItInsteadOfDownloading()
    {
        var manager = Create();
        Directory.CreateDirectory(Path.GetDirectoryName(ModelPath)!);
        await File.WriteAllBytesAsync(ModelPath, _content);

        await manager.InstallAsync("test", CancellationToken.None);

        Assert.Equal("done", (await FinishedAsync()).GetProperty("state").GetString());
        Assert.Empty(_server.Requests);
        Assert.True(manager.IsInstalled("test"));
    }

    [Fact]
    public async Task ANewCatalogHashForTheSameFileNameAndSizeIsNotInstalled()
    {
        var manager = Create();
        await manager.InstallAsync("test", CancellationToken.None);
        await FinishedAsync();
        manager.Dispose();

        var changed = Create(sha: new string('a', 64));

        Assert.False(changed.IsInstalled("test"));
        Assert.Null(changed.Resolve("test"));
        Assert.True(File.Exists(ModelPath)); // Not hashed again or set aside: the stamp already says what it is.
        Assert.Equal(ModelCheck.NotInstalled, await changed.VerifyAsync("test", CancellationToken.None));
        Assert.True(File.Exists(ModelPath));
    }

    [Fact]
    public async Task AnInstalledFileWithFlippedBytesIsReportedDamagedByVerifyAndDownloadedAgain()
    {
        var manager = Create();
        await manager.InstallAsync("test", CancellationToken.None);
        await FinishedAsync();
        Assert.Equal(ModelCheck.Verified, await manager.VerifyAsync("test", CancellationToken.None));

        // Same size, a few bytes changed (disk damage, an interrupted copy, someone editing it).
        var bytes = await File.ReadAllBytesAsync(ModelPath);
        bytes[1000] ^= 0xFF;
        bytes[^7] ^= 0x5A;
        File.SetAttributes(ModelPath, FileAttributes.Normal);
        _sink.Clear();
        await File.WriteAllBytesAsync(ModelPath, bytes);
        File.SetLastWriteTimeUtc(ModelPath, DateTime.UtcNow.AddMinutes(1)); // a later write, even within one clock tick

        Assert.Equal(ModelCheck.Damaged, await manager.VerifyAsync("test", CancellationToken.None));

        Assert.False(manager.IsInstalled("test"));
        Assert.Null(manager.Resolve("test"));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(ModelPath)!, "ggml-test.bin.corrupt-*"));
        var report = Assert.Single(_sink.Payloads("models.progress"));
        Assert.Equal("failed", report.GetProperty("state").GetString());
        Assert.Contains("damaged", report.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Contains("Download it again", report.GetProperty("message").GetString(), StringComparison.Ordinal);

        _sink.Clear();
        await manager.InstallAsync("test", CancellationToken.None);
        await FinishedAsync();

        Assert.Equal(_content, await File.ReadAllBytesAsync(ModelPath));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(ModelPath)!, "ggml-test.bin.corrupt-*"));
        Assert.Equal(ModelCheck.Verified, await manager.VerifyAsync("test", CancellationToken.None));
    }

    [Fact]
    public async Task AModelThatIsNotInstalledIsNotVerified()
    {
        var manager = Create();

        Assert.Equal(ModelCheck.NotInstalled, await manager.VerifyAsync("test", CancellationToken.None));
        Assert.Equal(ModelCheck.NotInstalled, await manager.VerifyAsync("unknown", CancellationToken.None));
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
