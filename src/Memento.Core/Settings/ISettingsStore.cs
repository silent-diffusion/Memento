namespace Memento.Core.Settings;

public interface ISettingsStore
{
    /// <summary>The settings in effect. Defaults until <see cref="LoadAsync"/> has run.</summary>
    AppSettings Current { get; }

    /// <summary>Raised after a successful <see cref="UpdateAsync"/> that changed something.</summary>
    event EventHandler<SettingsChangedEventArgs>? Changed;

    /// <summary>Reads the settings file. A missing file yields defaults; an unreadable one is set aside and yields defaults.</summary>
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken);

    /// <summary>Applies <paramref name="update"/> to the current settings and writes the result atomically.</summary>
    Task<AppSettings> UpdateAsync(Func<AppSettings, AppSettings> update, CancellationToken cancellationToken);
}
