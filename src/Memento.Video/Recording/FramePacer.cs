using Memento.Video.Encoding;
using Memento.Video.Sources;

namespace Memento.Video.Recording;

/// <summary>
/// Turns a source's irregular pictures into a constant-frame-rate track on the recording's clock. Slot <c>k</c> is
/// due at <c>origin + k / fps</c>; at each due slot the source's latest picture is encoded with the slot's time
/// (a display only delivers a picture when something changes, so a still screen repeats its last picture, which
/// costs the encoder almost nothing). A slot the pacing thread reaches late is still written if it is less than one
/// frame late; older slots are skipped and counted as dropped, so the track never runs behind the clock. Nothing here
/// waits: the caller sleeps until <see cref="NextDueQpc"/>.
/// </summary>
internal sealed class FramePacer
{
    private readonly long _originQpc;
    private readonly long _periodTicks;
    private long _nextSlot;

    public FramePacer(long originQpc, int frameRate, long firstSlot = 0)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(frameRate, 1);
        _originQpc = originQpc;
        _periodTicks = 10_000_000L / frameRate;
        _nextSlot = firstSlot;
    }

    public long PeriodTicks => _periodTicks;

    /// <summary>QPC time the next slot is due.</summary>
    public long NextDueQpc => _originQpc + (_nextSlot * _periodTicks);

    public long FramesWritten { get; private set; }

    /// <summary>Slots skipped because the pacing thread or the encoder was late, or the encoder had no free surface.</summary>
    public long FramesDropped { get; private set; }

    /// <summary>Slots passed while the source had no picture (before the first one, or while paused); not drops.</summary>
    public long FramesIdle { get; private set; }

    /// <summary>Time (100 ns from the origin) of the last frame written; -1 before the first.</summary>
    public long LastWrittenTicks { get; private set; } = -1;

    /// <summary>
    /// Handles every slot due at <paramref name="nowQpc"/>: writes the newest one (if the source has a picture and is
    /// not paused) and counts the older ones as dropped.
    /// </summary>
    public VideoWriteResult? Tick(long nowQpc, IVideoSource source, IVideoTrackWriter writer, bool paused)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(writer);
        if (nowQpc < NextDueQpc)
        {
            return null;
        }

        var dueSlot = (nowQpc - _originQpc) / _periodTicks;
        var skipped = dueSlot - _nextSlot;
        _nextSlot = dueSlot + 1;
        if (paused || !source.TryGetLatest(out var frame))
        {
            FramesIdle += skipped + 1;
            return null;
        }

        FramesDropped += skipped;
        var timestamp = dueSlot * _periodTicks;
        var result = writer.WriteFrame(frame, timestamp);
        switch (result)
        {
            case VideoWriteResult.Written:
                FramesWritten++;
                LastWrittenTicks = timestamp;
                break;
            case VideoWriteResult.NoFreeSurface:
                FramesDropped++;
                break;
        }

        return result;
    }
}
