using System.Security.Cryptography;
using Memento.Core.Secrets;

namespace Memento.Core.Tests.Fakes;

/// <summary>A secret store whose writes fail the way DPAPI does when Windows refuses (a damaged user profile).</summary>
internal sealed class RefusingSecretStore : ISecretStore
{
    public bool HasKey(string provider) => false;

    public Task SetKeyAsync(string provider, string key, CancellationToken cancellationToken) => throw new CryptographicException("Key not valid for use in specified state.");

    public Task ClearKeyAsync(string provider, CancellationToken cancellationToken) => throw new IOException("The process cannot access the file because it is being used by another process.");

    public Task<string?> GetKeyAsync(string provider, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
}
