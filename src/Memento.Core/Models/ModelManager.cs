using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Formatting;
using Memento.Core.Host;
using Memento.Core.Projects;
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

    // One background check per model file state (id, size, last write): a file that could not be verified is not hashed
    // again on every IsInstalled call, but a file that changed is.
    private readonly ConcurrentDictionary<string, Task> _verifications = new(StringComparer.Ordinal);

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
        if (Check(entry).Integrity == Integrity.Verified)
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
                failure is InvalidDataException
                    ? failure.Message
                    : $"{entry.Name} could not be downloaded: {Cause(failure)}. Nothing was installed. Check the internet connection, then install it again.",
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

        DeleteStamp(path);
        DeleteSetAside(entry);
        DeletePart(entry);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Hashes the installed file now (on a pool thread) unless it is already verified: a match is recorded beside it, a
    /// mismatch is set aside as <c>&lt;file&gt;.corrupt-&lt;time&gt;</c> so it is never loaded.
    /// </summary>
    /// <returns>Whether the model is installed and matches its catalog SHA-256.</returns>
    public Task<bool> VerifyAsync(string modelId, CancellationToken cancellationToken)
    {
        var entry = Find(modelId);
        return Task.Run(async () => await VerifyFileAsync(entry, cancellationToken) == Integrity.Verified, cancellationToken);
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

    /// <summary>
    /// Installed means the file is there at the catalog size and its stamp says it hashed to the catalog SHA-256 at
    /// its current size and last-write time. A file without a valid stamp (installed by an older version, or changed
    /// since) is hashed once in the background and does not count until it matched; this call never hashes.
    /// </summary>
    private bool IsInstalled(ModelCatalogEntry entry)
    {
        var (integrity, info) = Check(entry);
        if (integrity == Integrity.Unverified)
        {
            StartVerification(entry, info!);
        }

        return integrity == Integrity.Verified;
    }

    private (Integrity Integrity, FileInfo? Info) Check(ModelCatalogEntry entry)
    {
        var path = PathOf(entry);
        var info = new FileInfo(path);
        if (!info.Exists || info.Length != entry.SizeBytes)
        {
            return (Integrity.Missing, null);
        }

        var stamp = ReadStamp(path);
        if (stamp is null || stamp.SizeBytes != info.Length || stamp.LastWriteUtc != info.LastWriteTimeUtc)
        {
            return (Integrity.Unverified, info);
        }

        // The file is the one that was hashed; if that hash is not the catalog's, it is another model (the catalog changed).
        return (string.Equals(stamp.Sha256, entry.Sha256, StringComparison.Ordinal) ? Integrity.Verified : Integrity.Different, info);
    }

    private ModelVerifiedStamp? ReadStamp(string modelPath)
    {
        var path = modelPath + ModelVerifiedStamp.Suffix;
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var stamp = JsonSerializer.Deserialize(File.ReadAllBytes(path), ModelJsonContext.Default.ModelVerifiedStamp);
            return stamp is { SchemaVersion: ModelVerifiedStamp.CurrentSchemaVersion } ? stamp : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            LogStampUnreadable(ex, path);
            return null;
        }
    }

    private static async Task WriteStampAsync(string modelPath, string sha256, CancellationToken cancellationToken)
    {
        var info = new FileInfo(modelPath);
        var stamp = new ModelVerifiedStamp { Sha256 = sha256, SizeBytes = info.Length, LastWriteUtc = info.LastWriteTimeUtc };
        await AtomicJsonFile.WriteAsync(modelPath + ModelVerifiedStamp.Suffix, stamp, ModelJsonContext.Default.ModelVerifiedStamp, cancellationToken);
    }

    private void DeleteStamp(string modelPath)
    {
        try
        {
            File.Delete(modelPath + ModelVerifiedStamp.Suffix);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogPartNotRemoved(ex, modelPath + ModelVerifiedStamp.Suffix);
        }
    }

    private void StartVerification(ModelCatalogEntry entry, FileInfo info)
    {
        if (_downloads.ContainsKey(entry.Id))
        {
            return; // The download replaces the file and stamps it.
        }

        var key = string.Create(CultureInfo.InvariantCulture, $"{entry.Id}|{info.Length}|{info.LastWriteTimeUtc.Ticks}");
        _verifications.GetOrAdd(key, _ => Task.Run(() => VerifyInBackgroundAsync(entry), CancellationToken.None));
    }

    private async Task VerifyInBackgroundAsync(ModelCatalogEntry entry)
    {
        try
        {
            switch (await VerifyFileAsync(entry, CancellationToken.None))
            {
                case Integrity.Verified:
                    Publish(entry, StateDone, entry.SizeBytes, null);
                    Installed?.Invoke(this, entry.Id);
                    break;
                case Integrity.Different:
                    Publish(entry, StateFailed, 0, $"The installed file of {entry.Name} did not match its published checksum, so Memento set it aside and will not use it. Your recordings are not affected. Install the model again in Settings.");
                    break;
            }
        }
#pragma warning disable CA1031 // A background check must never take the app down; it is logged and the model reads as not installed.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogVerifyFailed(ex, entry.Id);
        }
    }

    /// <summary>
    /// Hashes the installed file unless its stamp already vouches for it; stamps a match, sets a mismatch aside.
    /// </summary>
    /// <returns><see cref="Integrity.Verified"/>, <see cref="Integrity.Missing"/>, <see cref="Integrity.Different"/>
    /// (a stamped other file, or a mismatch now set aside) or <see cref="Integrity.Unverified"/> (it changed while it
    /// was hashed).</returns>
    private async Task<Integrity> VerifyFileAsync(ModelCatalogEntry entry, CancellationToken cancellationToken)
    {
        var (integrity, before) = Check(entry);
        if (integrity != Integrity.Unverified)
        {
            return integrity;
        }

        var path = PathOf(entry);
        string hash;
        await using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: true))
        {
            hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
        }

        var after = new FileInfo(path);
        if (!after.Exists || after.Length != before!.Length || after.LastWriteTimeUtc != before.LastWriteTimeUtc)
        {
            return Integrity.Unverified; // Changed while it was being hashed; the next look starts over.
        }

        if (string.Equals(hash, entry.Sha256, StringComparison.Ordinal))
        {
            await WriteStampAsync(path, hash, cancellationToken);
            LogVerified(entry.Id, hash);
            return Integrity.Verified;
        }

        SetAside(entry, path, hash);
        return Integrity.Different;
    }

    private void SetAside(ModelCatalogEntry entry, string path, string hash)
    {
        var aside = path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        try
        {
            File.Move(path, aside, overwrite: true);
            LogSetAside(entry.Id, hash, aside);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogNotSetAside(ex, entry.Id, hash);
        }

        DeleteStamp(path);
    }

    private void DeleteSetAside(ModelCatalogEntry entry)
    {
        var path = PathOf(entry);
        var folder = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(folder))
        {
            return;
        }

        foreach (var aside in Directory.EnumerateFiles(folder, entry.FileName + ".corrupt-*"))
        {
            try
            {
                File.Delete(aside);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LogPartNotRemoved(ex, aside);
            }
        }
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

            // A file already in place without a valid stamp (an older version installed it) is hashed before anything
            // is downloaded: a match needs no download, a mismatch is set aside and downloaded again.
            if (Check(entry).Integrity == Integrity.Unverified)
            {
                download.Connected.TrySetResult(null);
                Publish(entry, StateVerifying, entry.SizeBytes, null);
            }

            if (await VerifyFileAsync(entry, token) != Integrity.Verified)
            {
                await DownloadAsync(download, token);
                await VerifyAndMoveAsync(download, token);
            }

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

        var response = await RequestAsync(entry, existing, cancellationToken);
        if (existing > 0 && !ContinuesAt(response, existing, entry.SizeBytes))
        {
            if (response.StatusCode == HttpStatusCode.PartialContent)
            {
                // A piece other than the one asked for (another offset, or another file's length): appending it would
                // splice two files together. Start over with the whole file.
                LogResumeRefused(entry.Id, existing, response.Content.Headers.ContentRange?.ToString() ?? "none");
                response.Dispose();
                File.Delete(part);
                response = await RequestAsync(entry, 0, cancellationToken);
            }

            existing = 0; // A 200 is the whole file: it replaces the part.
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.PartialContent && existing == 0)
            {
                throw new HttpRequestException("the server sent only part of the file when the whole file was asked for", null, response.StatusCode);
            }

            if (response.Content.Headers.ContentLength is { } length && existing + length != entry.SizeBytes)
            {
                if (existing > 0)
                {
                    DeletePart(entry);
                }

                throw new InvalidDataException(
                    $"The download server offers {HumanFormat.Bytes(existing + length)} for {entry.Name}, but the published file is {HumanFormat.Bytes(entry.SizeBytes)}, so it is not the right file. Nothing was installed or kept. Install it again later; if this keeps happening, the download server may have changed the file.");
            }

            var append = existing > 0;
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

    /// <summary>A 206 whose <c>Content-Range</c> starts exactly at <paramref name="from"/> of a file of the catalog size.</summary>
    private static bool ContinuesAt(HttpResponseMessage response, long from, long size) =>
        response.StatusCode == HttpStatusCode.PartialContent
        && response.Content.Headers.ContentRange is { HasRange: true, Unit: "bytes" } range
        && range.From == from
        && range.Length == size
        && (range.To is null || range.To == size - 1);

    /// <summary>Asks for the file from byte <paramref name="from"/>; returns a successful answer from an allowed host.</summary>
    private async Task<HttpResponseMessage> RequestAsync(ModelCatalogEntry entry, long from, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, entry.Url);
        if (from > 0)
        {
            request.Headers.Range = new RangeHeaderValue(from, null);
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

        try
        {
            // Redirects are followed by the handler; whatever answered must still be one of the model hosts.
            var answered = response.RequestMessage?.RequestUri;
            if (!ModelDownloadHosts.IsAllowedDownload(new Uri(entry.Url), answered))
            {
                throw new HttpRequestException(
                    $"the download server sent it on to {answered?.Host ?? "an unknown address"}{(answered is { Scheme: not "https" } ? " without https" : string.Empty)}, which is not one of the servers Memento downloads models from");
            }

            if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && from > 0)
            {
                DeletePart(entry);
                throw new HttpRequestException("the server refused to continue the earlier download; it will start over next time", null, response.StatusCode);
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(null, null, response.StatusCode);
            }

            return response;
        }
        catch
        {
            response.Dispose();
            throw;
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
        await WriteStampAsync(target, hash, cancellationToken);
        DeleteSetAside(entry);
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Download of model {ModelId} asked to continue at byte {From} but got range {Range}; starting over")]
    private partial void LogResumeRefused(string modelId, long from, string range);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Model stamp {Path} could not be read; the model is hashed again")]
    private partial void LogStampUnreadable(Exception exception, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Installed model {ModelId} hashed to {Hash}, not its catalog SHA-256; set aside as {Path}")]
    private partial void LogSetAside(string modelId, string hash, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Installed model {ModelId} hashed to {Hash}, not its catalog SHA-256, and could not be set aside")]
    private partial void LogNotSetAside(Exception exception, string modelId, string hash);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Installed model {ModelId} could not be verified")]
    private partial void LogVerifyFailed(Exception exception, string modelId);

    /// <summary>What is on disk for a model, from the cheapest look that can tell.</summary>
    private enum Integrity
    {
        /// <summary>No file, or not the catalog size.</summary>
        Missing,

        /// <summary>Its stamp matches the file and the catalog SHA-256.</summary>
        Verified,

        /// <summary>The right size but no valid stamp: it has to be hashed.</summary>
        Unverified,

        /// <summary>Its stamp matches the file but not the catalog SHA-256, or it was hashed and did not match.</summary>
        Different,
    }

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
