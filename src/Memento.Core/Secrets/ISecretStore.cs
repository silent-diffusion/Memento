namespace Memento.Core.Secrets;

/// <summary>
/// API keys for external AI providers, encrypted for the current Windows user (ARCHITECTURE.md §1, "Secrets").
/// Keys never appear in settings, logs, exports, project folders or bridge results; the UI only learns whether one
/// is saved.
/// </summary>
public interface ISecretStore
{
    bool HasKey(string provider);

    /// <summary>Saves or replaces the key for <paramref name="provider"/>.</summary>
    Task SetKeyAsync(string provider, string key, CancellationToken cancellationToken);

    /// <summary>Removes the key; nothing happens when none is saved.</summary>
    Task ClearKeyAsync(string provider, CancellationToken cancellationToken);

    /// <summary>The key, for the provider client that sends a request the user confirmed (M4). Never log it.</summary>
    Task<string?> GetKeyAsync(string provider, CancellationToken cancellationToken);
}
