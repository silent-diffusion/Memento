namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>settings.get</c> and <c>settings.set</c>. <see cref="LibraryPath"/> is the folder in effect.</summary>
public sealed record SettingsSnapshot(string Theme, string LibraryPath, string ListDensity, RecordingSettingsSnapshot Recording);
