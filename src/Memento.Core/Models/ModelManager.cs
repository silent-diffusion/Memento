using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Formatting;
using Memento.Core.Host;
using Microsoft.Extensions.Logging;

namespace Memento.Core.Models;

/// <summary>The <see cref="IModelManager"/>. See the interface for the rules it keeps.</summary>
public sealed partial class ModelManager : IModelManager, IDisposable
{
    public const string StateDownloading = "downloading";
    public const string StateVerifying = "verifying";
    public const string StateDone = "done";
    public const string StateFailed = "failed";

    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(250);

    private readonly ModelStoreOptions _options;
    private readonly ModelDownloadClient _client;
    private readonly IFreeSpaceProbe _freeSpace;
    private readonly BridgeEventPublisher _publisher;
    private readonly ILogger<ModelManager> _logger;
    private readonly SemaphoreSlim _downloadGate = new(1, 1);
    private readonly ConcurrentDictionary<string, Download> _downloads = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _inUse = new(StringComparer.Ordinal);

    public ModelManager(
        ModelCatalog catalog,
        ModelStoreOptions options,
        ModelDownloadClient client,
        IFreeSpaceProbe freeSpace,
        BridgeEventPublisher publisher,
        ILogger<ModelManager> logger)
    {
        Catalog = catalog;
        _options = options;
        _client = client;
        _freeSpace = freeSpace;
        _publisher = publisher;
        _logger = logger;
    }

    public event EventHandler<string>? Installed;

    public ModelCatalog Catalog { get; }

    public IReadOnlyList<ModelState> List() =>
        Catalog.Entries
            .Select(e => new ModelState(e, IsInstalled(e), _downloads.TryGetValue(e.Id, out var d) ? d.BytesDone : null))
            .ToList();

    public bool IsInstalled(string modelId) => Catalog.Find(modelId) is { } entry && IsInstalled(entry);

    public string? Resolve(string modelId) => Catalog.Find(modelId) is { } entry && IsInstalled(entry) ? PathOf(entry) : null;

    /// <summary>Where the model's file lives (whether or not it is there).</summary>
    public string PathOf(ModelCatalogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return Path.Combine(_options.Root, entry.Engine, entry.FileName);
    }

    public async Task InstallAsync(string modelId, CancellationToken cancellationToken)
    {
        var entry = Find(modelId);
        if (IsInstalled(entry))
        {
            Publish(entry, StateDone, entry.SizeBytes, null);
            return;
        }

        var target = PathOf(entry);
        var part = target + ".part";
        var partLength = File.Exists(part) ? new FileInfo(part).Length : 0;
        var needed = Math.Max(0, entry.SizeBytes - partLength) + _options.SpaceMarginBytes;
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (_freeSpace.GetFreeBytes(_options.Root) is { } free && free < needed)
        {
            throw new BridgeException(
                DomainErrorCodes.ModelsNoSpace,
                $"{entry.Name} needs {HumanFormat.Bytes(needed)} free on the drive that holds Memento's models, and there is {HumanFormat.Bytes(free)}. Nothing was downloaded. Free some space, then install it again.",
                string.Create(CultureInfo.InvariantCulture, $"{needed} bytes needed, {free} free in {_options.Root}"));
        }

        var download = new Download(entry);
        lock (_downloads)
        {
            if (_downloads.ContainsKey(entry.Id))
            {
                return; // Already downloading.
            }

            // One download at a time (BRIDGE.md M2 clarification 6): the other one is named.
            if (_downloads.Keys.FirstOrDefault() is { } running)
            {
                var other = Catalog.Find(running)?.Name ?? running;
                throw new BridgeException(
                    DomainErrorCodes.ModelsBusy,
                    $"{other} is downloading now, and Memento downloads one model at a time. Nothing was started; install {entry.Name} when that download has finished.",
                    running);
            }

            _downloads[entry.Id] = download;
        }

        download.Task = Task.Run(() => RunAsync(download), CancellationToken.None);

        // Answer the request with a specific error when the server cannot be reached at all.
        var connected = await download.Connected.Task.WaitAsync(_options.ConnectTimeout + TimeSpan.FromSeconds(5), cancellationToken)
            .ContinueWith(t => t.IsCompletedSuccessfully ? t.Result : null, TaskScheduler.Default);
        if (connected is { } failure)
        {
            throw new BridgeException(
                DomainErrorCodes.ModelsDownloadFailed,
                $"{entry.Name} could not be downloaded: {Cause(failure)}. Nothing was installed. Check the internet connection, then install it again.",
                Cause(failure));
        }
    }

    public async Task CancelInstallAsync(string modelId)
    {
        var entry = Find(modelId);
        if (_downloads.TryGetValue(entry.Id, out var download))
        {
            download.Cancelled = true;
            await download.Cancel.CancelAsync();
            if (download.Task is { } task)
            {
                await task.ContinueWith(_ => { }, TaskScheduler.Default);
            }
        }
        else
        {
            DeletePart(entry);
        }
    }

    public Task RemoveAsync(string modelId, CancellationToken cancellationToken)
    {
        var entry = Find(modelId);
        if (_inUse.TryGetValue(entry.Id, out var count) && count > 0)
        {
            throw new BridgeException(
                DomainErrorCodes.ModelsInUse,
                $"{entry.Name} is being used to process a recording, so it can't be removed now. It stays installed; remove it when processing has finished.",
                entry.Id);
        }

        if (_downloads.ContainsKey(entry.Id))
        {
            return CancelInstallAsync(entry.Id);
        }

        var path = PathOf(entry);
        if (File.Exists(path))
        {
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
            LogRemoved(entry.Id);
        }

        DeletePart(entry);
        return Task.CompletedTask;
    }

    public IDisposable Use(string modelId)
    {
        _inUse.AddOrUpdate(modelId, 1, (_, n) => n + 1);
        return new Lease(this, modelId);
    }

    public void Dispose()
    {
        foreach (var download in _downloads.Values)
        {
            download.Cancel.Cancel();
        }

        _downloadGate.Dispose();
    }

    private static string Cause(Exception exception) => exception switch
    {
        HttpRequestException { StatusCode: { } status } => string.Create(CultureInfo.InvariantCulture, $"the server answered {(int)status} {status}"),
        HttpRequestException http when http.InnerException is { } inner => inner.Message.TrimEnd('.'),
        TimeoutException => "the server did not answer in time",
        _ => exception.Message.TrimEnd('.'),
    };

    private bool IsInstalled(ModelCatalogEntry entry)
    {
        var path = PathOf(entry);
        return File.Exists(path) && new FileInfo(path).Length == entry.SizeBytes;
    }

    private ModelCatalogEntry Find(string modelId) =>
        Catalog.Find(modelId) ?? throw new BridgeException(
            DomainErrorCodes.ModelsNotFound,
            $"There is no model called '{modelId}' in this version of Memento. Nothing was changed. Choose one of the models listed in Settings.",
            modelId);

    private void DeletePart(ModelCatalogEntry entry)
    {
        var part = PathOf(entry) + ".part";
        try
        {
            File.Delete(part);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogPartNotRemoved(ex, part);
        }
    }

    private async Task RunAsync(Download download)
    {
        var entry = download.Entry;
        var token = download.Cancel.Token;
        var gateTaken = false;
        try
        {
            await _downloadGate.WaitAsync(token);
            gateTaken = true;
            await DownloadAsync(download, token);
            await VerifyAndMoveAsync(download, token);
            Publish(entry, StateDone, entry.SizeBytes, null);
            LogInstalled(entry.Id, entry.SizeBytes);
            _downloads.TryRemove(entry.Id, out _);
            Installed?.Invoke(this, entry.Id);
        }
        catch (OperationCanceledException) when (download.Cancelled)
        {
            DeletePart(entry);
            download.Connected.TrySetResult(null);
            Publish(entry, StateFailed, 0, $"The download of {entry.Name} was cancelled; the partial file was removed.");
            LogCancelled(entry.Id);
        }
#pragma warning disable CA1031 // A failed download is reported to the UI; the partial file stays so the next attempt resumes.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            download.Connected.TrySetResult(ex);
            var kept = File.Exists(PathOf(entry) + ".part") ? new FileInfo(PathOf(entry) + ".part").Length : 0;
            var message = ex is InvalidDataException
                ? ex.Message
                : $"The download of {entry.Name} stopped: {Cause(ex)}. "
                    + (kept > 0 ? $"{HumanFormat.Bytes(kept)} of {HumanFormat.Bytes(entry.SizeBytes)} are kept, and installing it again continues from there." : "Nothing was kept; install it again to retry.");
            Publish(entry, StateFailed, kept, message);
            LogFailed(ex, entry.Id);
        }
        finally
        {
            _downloads.TryRemove(entry.Id, out _);
            if (gateTaken)
            {
                _downloadGate.Release();
            }
        }
    }

    private async Task DownloadAsync(Download download, CancellationToken cancellationToken)
    {
        var entry = download.Entry;
        var part = PathOf(entry) + ".part";
        var existing = File.Exists(part) ? new FileInfo(part).Length : 0;
        if (existing > entry.SizeBytes)
        {
            File.Delete(part);
            existing = 0;
        }

        if (existing == entry.SizeBytes)
        {
            download.Connected.TrySetResult(null);
            return;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, entry.Url);
        if (existing > 0)
        {
            request.Headers.Range = new RangeHeaderValue(existing, null);
        }

        HttpResponseMessage response;
        using (var connect = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            connect.CancelAfter(_options.ConnectTimeout);
            try
            {
                response = await _client.Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, connect.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("The download server did not answer in time.");
            }
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && existing > 0)
            {
                File.Delete(part);
                throw new HttpRequestException("the server refused to continue the earlier download; it will start over next time", null, response.StatusCode);
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(null, null, response.StatusCode);
            }

            var append = existing > 0 && response.StatusCode == HttpStatusCode.PartialContent;
            if (!append)
            {
                existing = 0;
            }

            download.Connected.TrySetResult(null);
            download.BytesDone = existing;
            Publish(entry, StateDownloading, existing, null);
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var target = new FileStream(part, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16, useAsync: true);
            var buffer = new byte[1 << 20];
            var lastPublished = Stopwatch.StartNew();
            while (true)
            {
                int read;
                using (var stall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    stall.CancelAfter(_options.StallTimeout);
                    try
                    {
                        read = await source.ReadAsync(buffer, stall.Token);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        throw new TimeoutException("No data arrived for a minute.");
                    }
                }

                if (read == 0)
                {
                    break;
                }

                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                download.BytesDone += read;
                if (download.BytesDone > entry.SizeBytes)
                {
                    throw new InvalidDataException($"The download of {entry.Name} is larger than expected ({HumanFormat.Bytes(entry.SizeBytes)}), so it is not the right file. The partial file was removed; install it again to retry.");
                }

                if (lastPublished.Elapsed >= ProgressInterval)
                {
                    lastPublished.Restart();
                    Publish(entry, StateDownloading, download.BytesDone, null);
                }
            }

            await target.FlushAsync(cancellationToken);
        }

        if (download.BytesDone < entry.SizeBytes)
        {
            throw new IOException($"the connection closed after {HumanFormat.Bytes(download.BytesDone)} of {HumanFormat.Bytes(entry.SizeBytes)}");
        }
    }

    private async Task VerifyAndMoveAsync(Download download, CancellationToken cancellationToken)
    {
        var entry = download.Entry;
        var target = PathOf(entry);
        var part = target + ".part";
        Publish(entry, StateVerifying, entry.SizeBytes, null);
        string hash;
        await using (var stream = new FileStream(part, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: true))
        {
            hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
        }

        if (!string.Equals(hash, entry.Sha256, StringComparison.Ordinal))
        {
            File.Delete(part);
            throw new InvalidDataException(
                $"The download of {entry.Name} did not match its published checksum, so it was not installed and the file was removed. Install it again; if this keeps happening, the download server may have changed the file.");
        }

        File.Move(part, target, overwrite: true);
        LogVerified(entry.Id, hash);
    }

    private void Publish(ModelCatalogEntry entry, string state, long bytesDone, string? message)
    {
        var percent = entry.SizeBytes <= 0 ? 0 : (int)Math.Clamp(100L * bytesDone / entry.SizeBytes, 0, 100);
        _publisher.PublishModelsProgress(new ModelsProgressPayload(entry.Id, percent, bytesDone, entry.SizeBytes, state, message));
    }

    private void Release(string modelId) => _inUse.AddOrUpdate(modelId, 0, (_, n) => Math.Max(0, n - 1));

    [LoggerMessage(Level = LogLevel.Information, Message = "Model {ModelId} installed ({Bytes} bytes)")]
    private partial void LogInstalled(string modelId, long bytes);

    [LoggerMessage(Level = LogLevel.Information, Message = "Model {ModelId} verified: SHA-256 {Hash}")]
    private partial void LogVerified(string modelId, string hash);

    [LoggerMessage(Level = LogLevel.Information, Message = "Model {ModelId} removed")]
    private partial void LogRemoved(string modelId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Download of model {ModelId} cancelled")]
    private partial void LogCancelled(string modelId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Download of model {ModelId} failed")]
    private partial void LogFailed(Exception exception, string modelId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Partial download {Path} could not be removed")]
    private partial void LogPartNotRemoved(Exception exception, string path);

    private sealed class Download(ModelCatalogEntry entry)
    {
        public ModelCatalogEntry Entry { get; } = entry;

        public CancellationTokenSource Cancel { get; } = new();

        /// <summary>Completes with <c>null</c> once connected (or queued), or with the failure.</summary>
        public TaskCompletionSource<Exception?> Connected { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task? Task { get; set; }

        public long BytesDone { get; set; }

        public bool Cancelled { get; set; }
    }

    private sealed class Lease(ModelManager owner, string modelId) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.Release(modelId);
            }
        }
    }
}
