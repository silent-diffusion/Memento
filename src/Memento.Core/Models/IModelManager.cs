namespace Memento.Core.Models;

/// <summary>
/// Downloads, verifies, stores and removes models (ARCHITECTURE.md §1). Models live under
/// <c>%LOCALAPPDATA%\Memento\models\&lt;engine&gt;\</c>; a download is written to <c>&lt;file&gt;.part</c>, can be
/// resumed, is checked against the catalog's SHA-256 and only then moved into place.
/// </summary>
public interface IModelManager
{
    ModelCatalog Catalog { get; }

    /// <summary>Raised (on a pool thread) when a model finished installing; the argument is its id.</summary>
    event EventHandler<string>? Installed;

    /// <summary>Every catalog model with its state, read from disk now.</summary>
    IReadOnlyList<ModelState> List();

    bool IsInstalled(string modelId);

    /// <summary>The installed file, or <c>null</c> when the model is unknown or not installed.</summary>
    string? Resolve(string modelId);

    /// <summary>
    /// Starts installing (one download at a time). Returns once the download has connected; progress arrives as
    /// <c>models.progress</c>.
    /// </summary>
    /// <exception cref="Bridge.BridgeException"><c>models.notFound</c>, <c>models.busy</c> (another model is downloading), <c>models.noSpace</c> or <c>models.downloadFailed</c>.</exception>
    Task InstallAsync(string modelId, CancellationToken cancellationToken);

    /// <summary>Stops a download (or takes it out of the queue) and removes the partial file.</summary>
    Task CancelInstallAsync(string modelId);

    /// <summary>Deletes an installed model.</summary>
    /// <exception cref="Bridge.BridgeException"><c>models.notFound</c> or <c>models.inUse</c>.</exception>
    Task RemoveAsync(string modelId, CancellationToken cancellationToken);

    /// <summary>Marks the model as in use until the lease is disposed, so it cannot be removed meanwhile.</summary>
    IDisposable Use(string modelId);
}
