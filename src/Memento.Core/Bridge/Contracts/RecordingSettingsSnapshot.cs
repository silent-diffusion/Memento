namespace Memento.Core.Bridge.Contracts;

/// <summary>Settings › Recording in <see cref="SettingsSnapshot"/>.</summary>
/// <param name="DefaultSourceIds">The remembered source selection.</param>
/// <param name="KeepSeparateTracks">Always true in M1.</param>
/// <param name="CheckpointSeconds">5–300; 30 by default.</param>
/// <param name="LowSpaceGb">1–500; 10 by default.</param>
public sealed record RecordingSettingsSnapshot(
    string DefaultType,
    IReadOnlyList<string> DefaultSourceIds,
    bool KeepSeparateTracks,
    StorageSettingsSnapshot Storage,
    int CheckpointSeconds,
    int LowSpaceGb);
