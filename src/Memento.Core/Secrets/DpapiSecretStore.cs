using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Memento.Core.Secrets;

/// <summary>
/// <see cref="ISecretStore"/> with Windows DPAPI (<see cref="ProtectedData"/>, current-user scope). File layout:
/// the four bytes <c>MSEC</c>, a format version byte (1), then the DPAPI blob of a versioned JSON document. Writes go
/// to <c>secrets.bin.tmp</c>, are flushed, then moved over the file. A file this user cannot decrypt (copied from
/// another account, damaged) reads as "no keys" and is replaced on the next save. Nothing here is ever logged except
/// that the file could not be read.
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
            var document = Read();
            known = document.Keys.Keys.ToHashSet(StringComparer.Ordinal);
            Clear(document);
            Volatile.Write(ref _providersWithKeys, known);
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
            var document = Read();
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
            var document = Read(forUpdate: true);
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

    /// <param name="forUpdate">
    /// Reading to change one key and write the file back: a file that cannot be read right now (locked, no access)
    /// throws, so the other provider's key is never overwritten with nothing; a file whose content cannot be used
    /// (another format, a newer Memento, DPAPI refusing it) is kept beside as <c>secrets.bin.unreadable</c>.
    /// </param>
    private SecretsDocument Read(bool forUpdate = false)
    {
        if (!File.Exists(_options.FilePath))
        {
            return new SecretsDocument();
        }

        byte[]? plain = null;
        try
        {
            var bytes = File.ReadAllBytes(_options.FilePath);
            if (bytes.Length <= Magic.Length + 1 || !bytes.AsSpan(0, Magic.Length).SequenceEqual(Magic) || bytes[Magic.Length] != FormatVersion)
            {
                LogUnreadable("not a Memento secrets file of a known version");
                return Unusable(forUpdate);
            }

            plain = Unprotect(bytes[(Magic.Length + 1)..]);
            var document = JsonSerializer.Deserialize(plain, SecretsJsonContext.Default.SecretsDocument);
            if (document is null || document.SchemaVersion > SecretsDocument.CurrentSchemaVersion)
            {
                LogUnreadable("written by a newer Memento");
                return Unusable(forUpdate);
            }

            return document with { Keys = new Dictionary<string, string>(document.Keys, StringComparer.Ordinal) };
        }
        catch (Exception ex) when (forUpdate && ex is IOException or UnauthorizedAccessException)
        {
            LogUnreadable(ex.GetType().Name);
            throw;
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or IOException or UnauthorizedAccessException)
        {
            // The exception text is safe (it never carries the key) but the type is all that is needed.
            LogUnreadable(ex.GetType().Name);
            return Unusable(forUpdate && ex is CryptographicException or JsonException);
        }
        finally
        {
            if (plain is not null)
            {
                CryptographicOperations.ZeroMemory(plain);
            }
        }
    }

    /// <summary>An empty document; before an update replaces an unusable file, that file is kept beside it.</summary>
    private SecretsDocument Unusable(bool setAside)
    {
        if (setAside)
        {
            try
            {
                File.Copy(_options.FilePath, _options.FilePath + ".unreadable", overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LogUnreadable(ex.GetType().Name);
            }
        }

        return new SecretsDocument();
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
}
