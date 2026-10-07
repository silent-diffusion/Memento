namespace Memento.Core.Bridge.Contracts;

/// <summary>Settings › Documents › History in <see cref="SettingsSnapshot"/>.</summary>
public sealed record HistorySettingsSnapshot(bool KeepVersions, int KeepDays);
