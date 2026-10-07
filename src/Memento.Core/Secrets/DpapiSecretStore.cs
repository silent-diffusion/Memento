using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Memento.Core.Secrets;

/// <summary>
/// <see cref="ISecretStore"/> with Windows DPAPI (<see cref="ProtectedData"/>, current-user scope). File layout:
/// the four bytes <c>MSEC</c>, a format version byte (1), then the DPAPI blob of a versioned JSON document. Writes go
/// to <c>secrets.bin.tmp</c>, are flushed, then moved over the file. A file this user cannot decrypt (copied from
/// another account, damaged, or written by a newer Memento) reads as "no keys"; the next save first moves it to
/// <c>secrets.bin.unreadable-&lt;time&gt;</c>. A file that cannot be opened right now (another program holds it) is
/// never rewritten: the save fails with the <see cref="IOException"/>, so the other provider's key is not lost. Nothing
/// here is ever logged except that the file could not be read.
/// </summary>
public sealed partial class DpapiSecretStore : ISecretStore, IDisposable
{
    public const byte FormatVersion = 1;

    private static readonly byte[] Magic = "MSEC"u8.ToArray();

    // Ties the blob to Memento: another program running as the same user cannot decrypt it by accident.
    private static readonly byte[] Entropy = "Memento.Secrets.v1"u8.ToArray();

    private readonly SecretStoreOptions _options;
    private readonly ILogger<DpapiSecretStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private HashSet<string>? _providersWithKeys;

    public DpapiSecretStore(SecretStoreOptions options, ILogger<DpapiSecretStore> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options;
        _logger = logger;
    }

    public string FilePath => _options.FilePath;

    public bool HasKey(string provider)
    {
        var known = Volatile.Read(ref _providersWithKeys);
        if (known is null)
        {
            var document = Read(rethrowBusy: false, out var status);
            known = document.Keys.Keys.ToHashSet(StringComparer.Ordinal);
            Clear(document);

            // A file another program holds for a moment says nothing about which keys are saved: look again next time.
            if (status != ReadStatus.Busy)
            {
                Volatile.Write(ref _providersWithKeys, known);
            }
        }

        return known.Contains(provider);
    }

    public async Task SetKeyAsync(string provider, string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        await UpdateAsync(keys => keys[provider] = key, cancellationToken);
    }

    public async Task ClearKeyAsync(string provider, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        await UpdateAsync(keys => keys.Remove(provider), cancellationToken);
    }

    public async Task<string?> GetKeyAsync(string provider, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var document = Read(rethrowBusy: false, out _);
            return document.Keys.TryGetValue(provider, out var key) ? key : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private static void Clear(SecretsDocument document) => document.Keys.Clear();

    private async Task UpdateAsync(Action<Dictionary<string, string>> change, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            // A file that cannot be opened right now (another program holds it) is not rewritten: the write would keep
            // only this key and lose the others. The IOException reaches the caller, which reports that nothing was saved.
            var document = Read(rethrowBusy: true, out var status);
            if (status == ReadStatus.Unreadable)
            {
                // Damaged, from another Windows account, or from a newer Memento: keep it aside rather than overwrite it.
                SetAside();
            }

            change(document.Keys);
            await WriteAsync(document, cancellationToken);
            Volatile.Write(ref _providersWithKeys, document.Keys.Keys.ToHashSet(StringComparer.Ordinal));
            Clear(document);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Reads the keys; an empty document when there is no file or it cannot be read now (<paramref name="status"/> says which).</summary>
    /// <param name="rethrowBusy">Throw the <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> of a file that exists but cannot be opened now, instead of reading it as empty.</param>
    private SecretsDocument Read(bool rethrowBusy, out ReadStatus status)
    {
        if (!File.Exists(_options.FilePath))
        {
            status = ReadStatus.Missing;
            return new SecretsDocument();
        }

        byte[]? plain = null;
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(_options.FilePath);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            status = ReadStatus.Missing;
            return new SecretsDocument();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A sharing violation (a virus scanner, a backup) or a denied read: the file may hold good keys.
            LogBusy(ex.GetType().Name);
            if (rethrowBusy)
            {
                throw;
            }

            status = ReadStatus.Busy;
            return new SecretsDocument();
        }

        try
        {
            if (bytes.Length <= Magic.Length + 1 || !bytes.AsSpan(0, Magic.Length).SequenceEqual(Magic) || bytes[Magic.Length] != FormatVersion)
            {
                LogUnreadable("not a Memento secrets file of a known version");
                status = ReadStatus.Unreadable;
                return new SecretsDocument();
            }

            plain = Unprotect(bytes[(Magic.Length + 1)..]);
            var document = JsonSerializer.Deserialize(plain, SecretsJsonContext.Default.SecretsDocument);
            if (document is null || document.SchemaVersion > SecretsDocument.CurrentSchemaVersion)
            {
                LogUnreadable("written by a newer Memento");
                status = ReadStatus.Unreadable;
                return new SecretsDocument();
            }

            status = ReadStatus.Read;
            return document with { Keys = new Dictionary<string, string>(document.Keys, StringComparer.Ordinal) };
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException)
        {
            // The exception text is safe (it never carries the key) but the type is all that is needed.
            LogUnreadable(ex.GetType().Name);
            status = ReadStatus.Unreadable;
            return new SecretsDocument();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            if (plain is not null)
            {
                CryptographicOperations.ZeroMemory(plain);
            }
        }
    }

    /// <summary>Moves a file that cannot be read to <c>secrets.bin.unreadable-&lt;time&gt;</c>, so saving a key never destroys it.</summary>
    private void SetAside()
    {
        var aside = _options.FilePath + ".unreadable-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        File.Move(_options.FilePath, aside, overwrite: true);
        LogSetAside(Path.GetFileName(aside));
    }

    private async Task WriteAsync(SecretsDocument document, CancellationToken cancellationToken)
    {
        var plain = JsonSerializer.SerializeToUtf8Bytes(document, SecretsJsonContext.Default.SecretsDocument);
        byte[] blob;
        try
        {
            blob = Protect(plain);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_options.FilePath)!);
        var temporary = _options.FilePath + ".tmp";
        await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
        {
            await stream.WriteAsync(Magic, cancellationToken);
            await stream.WriteAsync(new[] { FormatVersion }, cancellationToken);
            await stream.WriteAsync(blob, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temporary, _options.FilePath, overwrite: true);
    }

    private static byte[] Protect(byte[] plain) =>
        OperatingSystem.IsWindows()
            ? ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser)
            : throw new PlatformNotSupportedException("API keys are stored with Windows DPAPI, which needs Windows.");

    private static byte[] Unprotect(byte[] blob) =>
        OperatingSystem.IsWindows()
            ? ProtectedData.Unprotect(blob, Entropy, DataProtectionScope.CurrentUser)
            : throw new PlatformNotSupportedException("API keys are stored with Windows DPAPI, which needs Windows.");

    [LoggerMessage(Level = LogLevel.Warning, Message = "The saved API keys could not be read ({Reason}); they read as not saved until a key is saved again")]
    private partial void LogUnreadable(string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The saved API keys could not be opened now ({Reason}); nothing was changed")]
    private partial void LogBusy(string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The unreadable API key file was kept as {FileName} before a new key was saved")]
    private partial void LogSetAside(string fileName);

    private enum ReadStatus
    {
        /// <summary>There is no file: no keys are saved.</summary>
        Missing,

        /// <summary>The file was decrypted and read.</summary>
        Read,

        /// <summary>The file exists but could not be opened now (sharing violation, denied); it may hold good keys.</summary>
        Busy,

        /// <summary>The file is damaged, from another Windows account, or from a newer Memento.</summary>
        Unreadable,
    }
}
