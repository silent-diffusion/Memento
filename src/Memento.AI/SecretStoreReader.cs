using Memento.Core.Secrets;

namespace Memento.AI;

/// <summary><see cref="ISecretReader"/> over Core's encrypted key store (<c>secrets.bin</c>, DPAPI current-user).</summary>
public sealed class SecretStoreReader(ISecretStore store) : ISecretReader
{
    private readonly ISecretStore _store = store ?? throw new ArgumentNullException(nameof(store));

    public bool HasKey(string provider) => _store.HasKey(provider);

    public Task<string?> GetKeyAsync(string provider, CancellationToken cancellationToken) => _store.GetKeyAsync(provider, cancellationToken);
}
