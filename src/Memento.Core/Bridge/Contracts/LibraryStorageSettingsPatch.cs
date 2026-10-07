using System.Text.Json;

namespace Memento.Core.Bridge.Contracts;

/// <summary>Settings › Storage and history for <c>settings.set</c>.</summary>
public sealed record LibraryStorageSettingsPatch
{
    /// <summary>Whole days (1–3650), or JSON <c>null</c> for none; left out, it keeps its value.</summary>
    public JsonElement ReclaimOlderThanDays { get; init; }
}
