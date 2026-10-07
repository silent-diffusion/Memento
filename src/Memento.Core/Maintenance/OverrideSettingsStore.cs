using Memento.Core.Settings;

namespace Memento.Core.Maintenance;

/// <summary>
/// The settings as they are, with the recording storage format replaced, so <c>storage.reclaim</c> can run the
/// optimize stage with the options the user chose for it without changing Settings › Recording.
/// </summary>
internal sealed class OverrideSettingsStore(ISettingsStore inner, StorageSettings storage) : ISettingsStore
{
    public AppSettings Current => inner.Current with { Recording = inner.Current.Recording with { Storage = storage } };

    public event EventHandler<SettingsChangedEventArgs>? Changed
    {
        add { }
        remove { }
    }

    public Task<AppSettings> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(Current);

    public Task<AppSettings> UpdateAsync(Func<AppSettings, AppSettings> update, CancellationToken cancellationToken) =>
        throw new NotSupportedException("The reclaim job never changes settings.");
}
