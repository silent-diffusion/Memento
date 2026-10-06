using System.Security.Cryptography;

namespace Memento.Core.Audio;

/// <summary>SHA-256 of files, as lowercase hex, streamed.</summary>
public static class FileHashes
{
    public static async Task<string> Sha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
