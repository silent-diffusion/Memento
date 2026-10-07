namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>settings.get</c> and <c>settings.set</c>. <see cref="LibraryPath"/> is the folder in effect.</summary>
/// <param name="Transcription">Settings › Transcription (M2).</param>
/// <param name="Speakers">Settings › Speakers (M2).</param>
/// <param name="History">Settings › Documents › History (M2).</param>
public sealed record SettingsSnapshot(
    string Theme,
    string LibraryPath,
    string ListDensity,
    RecordingSettingsSnapshot Recording,
    TranscriptionSettingsSnapshot Transcription,
    SpeakersSettingsSnapshot Speakers,
    HistorySettingsSnapshot History)
{
    /// <summary>Settings › General (M3).</summary>
    public GeneralSettingsSnapshot General { get; init; } = new(false, false, "en");

    /// <summary>Settings › Export (M3).</summary>
    public ExportSettingsSnapshot Export { get; init; } = new(false, null, true, true, ExportSelection.Default);

    /// <summary>Settings › AI and privacy (M3), with which providers have a saved key (never the keys).</summary>
    public AiSettingsSnapshot Ai { get; init; } = new(false, true, true, new(true, true, true, true, true, false), new(new(false), new(false)));

    /// <summary>Settings › Storage and history (M3).</summary>
    public LibraryStorageSettingsSnapshot Storage { get; init; } = new(null);
}
