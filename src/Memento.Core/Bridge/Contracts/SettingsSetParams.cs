namespace Memento.Core.Bridge.Contracts;

/// <summary>
/// Parameters of <c>settings.set</c>: a partial update. Omitted (or <c>null</c>) fields keep their value.
/// </summary>
public sealed record SettingsSetParams
{
    /// <summary><c>"system"</c>, <c>"light"</c> or <c>"dark"</c>.</summary>
    public string? Theme { get; init; }

    /// <summary>Accepted only when it equals the current location; moving the library is a separate flow.</summary>
    public string? LibraryPath { get; init; }

    /// <summary><c>"comfortable"</c> or <c>"compact"</c>.</summary>
    public string? ListDensity { get; init; }

    /// <summary>Settings › Recording, replaced whole when present.</summary>
    public RecordingSettingsPatch? Recording { get; init; }

    /// <summary>Settings › Transcription; each field present is changed, the others keep their value.</summary>
    public TranscriptionSettingsPatch? Transcription { get; init; }

    /// <summary>Settings › Speakers; each field present is changed, the others keep their value.</summary>
    public SpeakersSettingsPatch? Speakers { get; init; }

    /// <summary>Settings › Documents › History; each field present is changed, the others keep their value.</summary>
    public HistorySettingsPatch? History { get; init; }

    /// <summary>Settings › General (M3); each field present is changed.</summary>
    public GeneralSettingsPatch? General { get; init; }

    /// <summary>Settings › Export (M3); each field present is changed, <c>defaults</c> replaces whole.</summary>
    public ExportSettingsPatch? Export { get; init; }

    /// <summary>Settings › AI and privacy (M3); each field present is changed. Keys go through <c>ai.setKey</c>.</summary>
    public AiSettingsPatch? Ai { get; init; }

    /// <summary>Settings › Storage and history (M3).</summary>
    public LibraryStorageSettingsPatch? Storage { get; init; }

    /// <summary>Settings › Documents (M4); each field present is changed.</summary>
    public DocumentsSettingsPatch? Documents { get; init; }
}
