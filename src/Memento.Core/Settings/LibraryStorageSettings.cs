using System.Text.Json;
using System.Text.Json.Serialization;

namespace Memento.Core.Settings;

/// <summary>Settings › Storage and history (M3): which recordings "Reclaim space" converts by default.</summary>
public sealed record LibraryStorageSettings
{
    public const int MinDays = 1;
    public const int MaxDays = 3650;

    /// <summary><c>null</c>: no age is set, so <c>storage.reclaim</c> needs recording ids.</summary>
    public int? ReclaimOlderThanDays { get; init; }

    /// <summary>
    /// 2.0, Settings › Storage and history › Remove video older than: recordings older than this many days lose their
    /// video files (audio, transcript and documents are kept); <c>null</c> for never, the default.
    /// </summary>
    public int? RemoveVideoOlderThanDays { get; init; }

    /// <summary>
    /// 2.1, Settings › Storage and history › Delete audio tracks older than: recordings older than this many days lose
    /// their audio (tracks, mix, peaks; the transcript, documents and video are kept). One of
    /// <see cref="Audio.AudioRemovalService.AgeChoices"/>, or <c>null</c> for never, the default.
    /// </summary>
    public int? DeleteAudioOlderThanDays { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    /// <summary>Returns the first problem, worded for people, or <c>null</c>.</summary>
    public string? Validate() =>
        ReclaimOlderThanDays is < MinDays or > MaxDays
            ? $"Recordings older than {ReclaimOlderThanDays} days is out of range. Choose {MinDays} to {MaxDays} days, or none."
            : RemoveVideoOlderThanDays is < MinDays or > MaxDays
                ? $"Removing video older than {RemoveVideoOlderThanDays} days is out of range. Choose {MinDays} to {MaxDays} days, or never."
                : DeleteAudioOlderThanDays is { } audio && !Audio.AudioRemovalService.AgeChoices.Contains(audio)
                    ? $"Deleting audio older than {audio} days is not one of the choices. Choose {string.Join(", ", Audio.AudioRemovalService.AgeChoices)} days, or never."
                    : null;
}
