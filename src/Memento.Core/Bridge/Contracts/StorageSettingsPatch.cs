namespace Memento.Core.Bridge.Contracts;

/// <summary>Storage settings inside the recording block; an omitted or <c>null</c> field takes its default.</summary>
public sealed record StorageSettingsPatch
{
    public string? Codec { get; init; }

    /// <summary>Applies to lossy codecs; switching to a lossy codec without one uses 192 kbps.</summary>
    public int? BitrateKbps { get; init; }

    public bool? DownmixMono { get; init; }

    public bool? KeepOnlyMix { get; init; }

    /// <summary>"Even out source levels" (after 2.0.0-beta.1); omitted or <c>null</c> takes the default, on.</summary>
    public bool? EvenLevels { get; init; }
}
