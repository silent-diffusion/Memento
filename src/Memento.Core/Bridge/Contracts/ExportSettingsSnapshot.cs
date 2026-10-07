namespace Memento.Core.Bridge.Contracts;

/// <summary>Settings › Export (M3).</summary>
/// <param name="SaveCopiesOutside">"Save copies outside Memento"; off by default.</param>
/// <param name="DefaultFolder">The default export folder, or <c>null</c> when none was chosen.</param>
/// <param name="Defaults">The Export dialog's ticks and formats by default.</param>
public sealed record ExportSettingsSnapshot(bool SaveCopiesOutside, string? DefaultFolder, bool AskWhereEachTime, bool CreateSubfolder, ExportSelection Defaults);
