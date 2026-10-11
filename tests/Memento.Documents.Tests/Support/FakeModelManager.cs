using Memento.Core.Models;

namespace Memento.Documents.Tests.Support;

/// <summary>A model manager whose installed files are given: the Tesseract language data at a path, or nothing.</summary>
internal sealed class FakeModelManager(IReadOnlyDictionary<string, string>? installed = null) : IModelManager
{
    private readonly Dictionary<string, string> _installed = new(installed ?? new Dictionary<string, string>(), StringComparer.Ordinal);

    public ModelCatalog Catalog => ModelCatalog.Default;

    /// <summary>What <see cref="VerifyAsync"/> answers for an installed model.</summary>
    public ModelCheck Check { get; set; } = ModelCheck.Verified;

    public int Leases { get; private set; }

    public int OpenLeases { get; private set; }

    public event EventHandler<string>? Installed
    {
        add { }
        remove { }
    }

    public IReadOnlyList<ModelState> List() => Catalog.Entries.Select(e => new ModelState(e, _installed.ContainsKey(e.Id), null)).ToList();

    public bool IsInstalled(string modelId) => _installed.ContainsKey(modelId);

    public string? Resolve(string modelId) => _installed.TryGetValue(modelId, out var path) ? path : null;

    public Task InstallAsync(string modelId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task CancelInstallAsync(string modelId) => Task.CompletedTask;

    public Task RemoveAsync(string modelId, CancellationToken cancellationToken)
    {
        _installed.Remove(modelId);
        return Task.CompletedTask;
    }

    public Task<ModelCheck> VerifyAsync(string modelId, CancellationToken cancellationToken) =>
        Task.FromResult(_installed.ContainsKey(modelId) ? Check : ModelCheck.NotInstalled);

    public IDisposable Use(string modelId)
    {
        Leases++;
        OpenLeases++;
        return new Lease(this);
    }

    private sealed class Lease(FakeModelManager owner) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                owner.OpenLeases--;
            }
        }
    }
}
