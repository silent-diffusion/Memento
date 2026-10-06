using System.Security.Cryptography;

namespace Memento.Audio.Codecs;

/// <summary>SHA-256 of files for the manifest's <c>integrity</c> block.</summary>
public static class FileHashing
{
    public static async Task<string> Sha256Async(string path, CancellationToken cancellationToken)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(file, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
