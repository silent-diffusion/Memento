namespace Memento.Core.Bridge.Contracts;

/// <summary>Settings › Recording for <c>settings.set</c>. The block replaces the stored one whole; an omitted or <c>null</c> field takes its default.</summary>
public sealed record RecordingSettingsPatch
{
    public string? DefaultType { get; init; }

    public IReadOnlyList<string>? DefaultSourceIds { get; init; }

    /// <summary>Only <c>true</c> is accepted in M1.</summary>
    public bool? KeepSeparateTracks { get; init; }

    public StorageSettingsPatch? Storage { get; init; }

    public int? CheckpointSeconds { get; init; }

    public int? LowSpaceGb { get; init; }

    /// <summary>2.0, Recording mode.</summary>
    public bool? CaptureVideo { get; init; }

    /// <summary>2.0, Video defaults; replaced whole with the block.</summary>
    public VideoSettingsPatch? Video { get; init; }
}
