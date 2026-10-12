using System.Text.Json;

namespace Memento.Core.Bridge.Contracts;

/// <summary>Settings › Storage and history for <c>settings.set</c>.</summary>
public sealed record LibraryStorageSettingsPatch
{
    /// <summary>Whole days (1–3650), or JSON <c>null</c> for none; left out, it keeps its value.</summary>
    public JsonElement ReclaimOlderThanDays { get; init; }

    /// <summary>2.0: whole days (1–3650), or JSON <c>null</c> for never; left out, it keeps its value.</summary>
    public JsonElement RemoveVideoOlderThanDays { get; init; }

    /// <summary>2.1: 1, 7, 30, 90, 365, 730, 1095, 1460 or 1825 days, or JSON <c>null</c> for never; left out, it keeps its value.</summary>
    public JsonElement DeleteAudioOlderThanDays { get; init; }
}
