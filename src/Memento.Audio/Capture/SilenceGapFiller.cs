namespace Memento.Audio.Capture;

/// <summary>
/// Keeps an endpoint-loopback track on the clock (ENGINE-NOTES.md §C): loopback delivers nothing while nothing
/// plays, so while idle we emit zero frames up to <c>now − holdback</c>, and when a real packet arrives we fill
/// any remaining gap before it (or trim its head if silence already covered that time).
/// <para>
/// Position is kept as an anchor time plus a frame count since the anchor, re-anchored at every packet with a
/// reliable timestamp, so rounding never accumulates and the device/QPC drift is never "corrected" by inserting
/// or trimming frames: jitter under <see cref="Threshold"/> is ignored. Pure arithmetic, used on the capture thread.
/// </para>
/// </summary>
internal sealed class SilenceGapFiller
{
    private readonly int _sampleRate;
    private long _anchorQpc;
    private long _framesSinceAnchor;

    /// <param name="sampleRate">Stream rate.</param>
    /// <param name="originQpc">Stream start (after <c>IAudioClient::Start</c>); silence is filled from here.</param>
    /// <param name="holdback">How far behind "now" idle filling stays, so a late real packet rarely overlaps synthesized silence.</param>
    /// <param name="threshold">Gaps or overlaps shorter than this are treated as timestamp jitter.</param>
    public SilenceGapFiller(int sampleRate, long originQpc, TimeSpan holdback, TimeSpan threshold)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        _sampleRate = sampleRate;
        _anchorQpc = originQpc;
        Holdback = holdback;
        Threshold = threshold;
    }

    public TimeSpan Holdback { get; }

    public TimeSpan Threshold { get; }

    /// <summary>Capture time just past the last frame emitted (real or synthesized).</summary>
    public long CursorQpc => _anchorQpc + QpcClock.FramesToTicks(_framesSinceAnchor, _sampleRate);

    public long SynthesizedFrames { get; private set; }

    public long TrimmedFrames { get; private set; }

    /// <summary>
    /// No packet is waiting: returns how many silent frames to emit now (0 if none), starting at
    /// <paramref name="silenceStartQpc"/>. Emits only whole chunks of at least <see cref="Threshold"/>.
    /// </summary>
    public int OnIdle(long nowQpc, out long silenceStartQpc)
    {
        silenceStartQpc = CursorQpc;
        var target = nowQpc - Holdback.Ticks;
        if (target - silenceStartQpc < Threshold.Ticks)
        {
            return 0;
        }

        var frames = QpcClock.TicksToFrames(target - _anchorQpc, _sampleRate) - _framesSinceAnchor;
        if (frames <= 0)
        {
            return 0;
        }

        var n = (int)Math.Min(frames, int.MaxValue / 2);
        _framesSinceAnchor += n;
        SynthesizedFrames += n;
        return n;
    }

    /// <summary>A real packet of <paramref name="frames"/> frames captured at <paramref name="qpc"/> arrived.</summary>
    public GapAdjustment OnPacket(long qpc, int frames, bool timestampReliable)
    {
        var cursor = CursorQpc;
        if (!timestampReliable)
        {
            // No trustworthy time: it simply follows what was emitted.
            _framesSinceAnchor += frames;
            return new GapAdjustment(0, cursor, 0, cursor);
        }

        var gap = qpc - cursor;
        var silence = 0;
        var trim = 0;
        if (gap >= Threshold.Ticks)
        {
            silence = (int)Math.Max(0, QpcClock.TicksToFramesRounded(qpc - _anchorQpc, _sampleRate) - _framesSinceAnchor);
        }
        else if (-gap >= Threshold.Ticks)
        {
            var overlap = _framesSinceAnchor - QpcClock.TicksToFramesRounded(qpc - _anchorQpc, _sampleRate);
            trim = (int)Math.Clamp(overlap, 0, frames);
        }

        SynthesizedFrames += silence;
        TrimmedFrames += trim;
        if (trim == frames)
        {
            // Entirely inside silence already emitted: drop it and keep the cursor where it is.
            return new GapAdjustment(0, cursor, trim, cursor);
        }

        _anchorQpc = qpc;
        _framesSinceAnchor = frames;
        var keptStart = qpc + QpcClock.FramesToTicks(trim, _sampleRate);
        return new GapAdjustment(silence, cursor, trim, keptStart);
    }
}
