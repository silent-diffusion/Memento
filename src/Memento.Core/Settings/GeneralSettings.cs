using System.Text.Json;
using System.Text.Json.Serialization;

namespace Memento.Core.Settings;

/// <summary>Settings › General (M3): startup and language. Theme and list density stay at the top level (M0).</summary>
public sealed record GeneralSettings
{
    public const string English = "en";

    public static IReadOnlyList<string> Languages { get; } = [English];

    /// <summary>Mirrors the Windows startup entry; <c>app.setStartup</c> changes both.</summary>
    public bool StartWithWindows { get; init; }

    /// <summary>Stored; applied in M5.</summary>
    public bool KeepRunningInTray { get; init; }

    public string Language { get; set; } = English;

    /// <summary>
    /// Check for updates at start and every 24 hours, and download them in the background (H1). Off: no automatic
    /// check reaches the network; Settings › General's Check now still does. Installing always waits for a click or
    /// the next start.
    /// </summary>
    public bool AutoUpdate { get; init; } = true;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    /// <summary>Returns the first problem, worded for people, or <c>null</c>.</summary>
    public string? Validate() =>
        Languages.Contains(Language, StringComparer.Ordinal)
            ? null
            : $"Language '{Language}' is not available in this version. Choose {string.Join(", ", Languages)}.";
}
