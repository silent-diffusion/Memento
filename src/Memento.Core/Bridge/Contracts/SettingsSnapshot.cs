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
    HistorySettingsSnapshot History);
