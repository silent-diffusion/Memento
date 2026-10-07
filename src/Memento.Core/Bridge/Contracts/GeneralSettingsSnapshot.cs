namespace Memento.Core.Bridge.Contracts;

/// <summary>Settings › General (M3). <see cref="KeepRunningInTray"/> is stored and applied in M5.</summary>
/// <param name="Language">Interface language; <c>en</c> is the only one in 1.0.</param>
/// <param name="AutoUpdate">"Install updates automatically" (H1): check at start and daily, download in the background.</param>
public sealed record GeneralSettingsSnapshot(bool StartWithWindows, bool KeepRunningInTray, string Language, bool AutoUpdate = true);
