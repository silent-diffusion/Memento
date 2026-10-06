using Memento.Audio.Capture;
using Memento.Audio.Sources;
using Memento.Audio.Writing;
using Microsoft.Extensions.Logging;

namespace Memento.Audio.Recording;

/// <summary>Everything <see cref="AudioRecordingSession.StartAsync"/> needs.</summary>
/// <param name="TracksDirectory">The project's <c>tracks/</c> folder (created if missing).</param>
/// <param name="SourceIds">Bridge source ids (<c>mic:…</c>, <c>system:…</c>, <c>app:…</c>); at least one.</param>
public sealed record AudioRecordingOptions(string TracksDirectory, IReadOnlyList<string> SourceIds)
{
    /// <summary>How often every writer is made durable and <see cref="AudioRecordingSession.Checkpointed"/> raised (ARCHITECTURE.md §5: 30 s).</summary>
    public TimeSpan CheckpointInterval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Level publication interval; clamped to at most 30 per second.</summary>
    public TimeSpan LevelInterval { get; init; } = TimeSpan.FromSeconds(1.0 / 30);

    public long RolloverBytes { get; init; } = RollingWavWriter.DefaultRolloverBytes;

    public bool DurableCheckpoints { get; init; } = true;

    /// <summary>
    /// Pad tracks added mid-session with silence back to the session start so every file starts at timeline 0.
    /// Off by default: such tracks report <see cref="TrackResult.StartOffset"/> instead (no large zero runs on disk).
    /// Tracks that start with the session are always padded over their start-up latency.
    /// </summary>
    public bool PadLateTracks { get; init; }

    /// <summary>How long stop waits for the last packets captured before the stop instant.</summary>
    public TimeSpan StopDrainTimeout { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Opens captures; defaults to WASAPI. Tests substitute synthetic sources.</summary>
    public IAudioCaptureFactory? CaptureFactory { get; init; }

    /// <summary>Names a source for its track; defaults to <see cref="AudioSourceEnumerator.Describe"/>.</summary>
    public Func<AudioSourceId, AudioSourceInfo?>? Describe { get; init; }

    public ILoggerFactory? LoggerFactory { get; init; }
}
