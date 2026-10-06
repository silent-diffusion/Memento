using Memento.Core.Settings;

namespace Memento.Core.Library;

/// <summary><see cref="ILibraryLocation"/> from <see cref="AppSettings.EffectiveLibraryPath"/>.</summary>
public sealed class SettingsLibraryLocation(ISettingsStore settings) : ILibraryLocation
{
    public string Root => settings.Current.EffectiveLibraryPath;
}
