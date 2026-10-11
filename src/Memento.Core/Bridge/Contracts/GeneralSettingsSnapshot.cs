namespace Memento.Core.Bridge.Contracts;

/// <summary>Settings › General (M3; the tray and Start with Windows are applied from 2.0).</summary>
/// <param name="KeepRunningInTray">Closing the window keeps Memento running in the notification area (2.0).</param>
/// <param name="Language">Interface language; <c>en</c> is the only one in 1.0.</param>
/// <param name="AutoUpdate">"Install updates automatically" (H1): check at start and daily, download in the background.</param>
/// <param name="StartWithWindowsAvailable">Only an installed copy may register itself (2.0); Settings disables the toggle otherwise.</param>
/// <param name="StartWithWindowsNote">Why it is not available, as a sentence; <c>null</c> when it is.</param>
/// <param name="DateFormat">2.1: how full dates read (Settings › General › Date format).</param>
/// <param name="LibraryView">2.1: the Library's Folders / Timeline switch: <c>timeline</c> or <c>folders</c>.</param>
public sealed record GeneralSettingsSnapshot(
    bool StartWithWindows,
    bool KeepRunningInTray,
    string Language,
    bool AutoUpdate = true,
    bool StartWithWindowsAvailable = true,
    string? StartWithWindowsNote = null,
    string DateFormat = Formatting.DateFormats.Default,
    string LibraryView = Settings.GeneralSettings.TimelineView);
