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

    // A setter rather than init: System.Text.Json cannot bind extension data through init-only members.
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    /// <summary>The library folder in effect: the configured one, or the default.</summary>
    [JsonIgnore]
    public string EffectiveLibraryPath => string.IsNullOrWhiteSpace(LibraryPath) ? AppPaths.DefaultLibrary : LibraryPath;
}
