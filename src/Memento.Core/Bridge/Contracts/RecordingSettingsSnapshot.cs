namespace Memento.Core.Bridge.Contracts;

/// <summary>Settings › Recording in <see cref="SettingsSnapshot"/>.</summary>
/// <param name="DefaultSourceIds">The remembered source selection.</param>
/// <param name="KeepSeparateTracks">Always true in M1.</param>
/// <param name="CheckpointSeconds">5–300; 30 by default.</param>
/// <param name="LowSpaceGb">1–500; 10 by default.</param>
/// <param name="CaptureVideo">2.0, Recording mode: false = Audio only (the default), true = Audio + video.</param>
/// <param name="Video">2.0, Video defaults.</param>
public sealed record RecordingSettingsSnapshot(
    string DefaultType,
    IReadOnlyList<string> DefaultSourceIds,
    bool KeepSeparateTracks,
    StorageSettingsSnapshot Storage,
    int CheckpointSeconds,
    int LowSpaceGb,
    bool CaptureVideo,
    VideoSettingsSnapshot Video);
