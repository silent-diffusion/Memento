namespace Memento.AI;

/// <summary>
/// Read-only access to the saved API keys (backed by Core's DPAPI <c>ISecretStore</c> through
/// <see cref="SecretStoreReader"/>). A provider reads the key for each request it sends and keeps it only for that
/// request; keys are never logged, put in exceptions or returned to callers.
/// </summary>
public interface ISecretReader
{
    bool HasKey(string provider);

    Task<string?> GetKeyAsync(string provider, CancellationToken cancellationToken);
}
