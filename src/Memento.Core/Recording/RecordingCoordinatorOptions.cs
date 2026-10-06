namespace Memento.Core.Recording;

/// <summary>Timings of <see cref="RecordingCoordinator"/>; tests shorten them.</summary>
public sealed record RecordingCoordinatorOptions
{
    /// <summary>Free space is sampled this often while recording (ARCHITECTURE.md §5.7): 5 s.</summary>
    public TimeSpan DiskSampleInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary><c>recording.state</c> is sent at least this often while recording: 1 s.</summary>
    public TimeSpan StateTickInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Below this much free space the recording stops cleanly before writes start failing: 256 MB.</summary>
    public long StopFloorBytes { get; init; } = 256L * 1024 * 1024;

    /// <summary><c>recording.levels</c> is sent at most once per this interval (≤ 30 per second).</summary>
    public TimeSpan LevelsMinInterval { get; init; } = TimeSpan.FromMilliseconds(33);
}
