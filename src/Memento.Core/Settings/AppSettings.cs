using System.Text.Json;
using System.Text.Json.Serialization;

namespace Memento.Core.Settings;

/// <summary>
/// The contents of <c>settings.json</c>. Fields this version does not know about are kept in
/// <see cref="ExtensionData"/> and written back unchanged, so a newer or older build never drops them.
/// </summary>
public sealed record AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>One of <see cref="ThemePreference"/>.</summary>
    public string Theme { get; init; } = ThemePreference.System;

    /// <summary>Library location; <c>null</c> means <see cref="AppPaths.DefaultLibrary"/>.</summary>
    public string? LibraryPath { get; init; }

    /// <summary>One of <see cref="Settings.ListDensity"/>.</summary>
    public string ListDensity { get; init; } = Settings.ListDensity.Comfortable;

    /// <summary>Settings › Recording (M1). Missing in M0 files, which then read with the defaults.</summary>
    public RecordingSettings Recording { get; init; } = new();

    /// <summary>Settings › Transcription (M2). Missing in M1 files, which then read with the defaults.</summary>
    public TranscriptionSettings Transcription { get; init; } = new();

    /// <summary>Settings › Speakers (M2).</summary>
    public SpeakerSettings Speakers { get; init; } = new();

    /// <summary>Settings › Documents › History (M2).</summary>
    public HistorySettings History { get; init; } = new();

    // The M3 blocks have setters, not init: source-generated JSON sets a missing init-only member to null.

    /// <summary>Settings › General (M3). Missing in M2 files, which then read with the defaults.</summary>
    public GeneralSettings General { get; set; } = new();

    /// <summary>Settings › Export (M3).</summary>
    public ExportSettings Export { get; set; } = new();

    /// <summary>Settings › AI and privacy (M3); keys are never stored here.</summary>
    public AiSettings Ai { get; set; } = new();

    /// <summary>Settings › Storage and history (M3).</summary>
    public LibraryStorageSettings Storage { get; set; } = new();

    // A setter rather than init: System.Text.Json cannot bind extension data through init-only members.
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    /// <summary>The library folder in effect: the configured one, or the default.</summary>
    [JsonIgnore]
    public string EffectiveLibraryPath => string.IsNullOrWhiteSpace(LibraryPath) ? AppPaths.DefaultLibrary : LibraryPath;
}
