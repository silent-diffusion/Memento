namespace Memento.Core.Bridge.Contracts;

/// <summary>Storage settings inside the recording block; an omitted or <c>null</c> field takes its default.</summary>
public sealed record StorageSettingsPatch
{
    public string? Codec { get; init; }

    /// <summary>Applies to lossy codecs; switching to a lossy codec without one uses 192 kbps.</summary>
    public int? BitrateKbps { get; init; }

    public bool? DownmixMono { get; init; }

    public bool? KeepOnlyMix { get; init; }
}
