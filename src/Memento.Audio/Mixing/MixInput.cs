using Memento.Audio.Codecs;
using Memento.Audio.Writing;

namespace Memento.Audio.Mixing;

/// <summary>
/// One track in a mixdown, placed on the recording timeline: it starts at <see cref="StartOffset"/> and, if it
/// ended early (source lost or disabled), contributes nothing after <see cref="EndedAt"/>.
/// </summary>
public sealed class MixInput
{
    private readonly Func<DecodedAudio> _open;

    private MixInput(string name, Func<DecodedAudio> open, TimeSpan startOffset, TimeSpan? endedAt, float gain)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(startOffset, TimeSpan.Zero);
        if (endedAt is { } end && end < startOffset)
        {
            throw new ArgumentOutOfRangeException(nameof(endedAt), "A track cannot end before it starts.");
        }

        Name = name;
        _open = open;
        StartOffset = startOffset;
        EndedAt = endedAt;
        Gain = gain;
    }

    public string Name { get; }

    /// <summary>Where the track's first frame sits on the timeline (start-up latency, or a source added mid-session).</summary>
    public TimeSpan StartOffset { get; }

    /// <summary>Timeline time the track ended early, or null.</summary>
    public TimeSpan? EndedAt { get; }

    public float Gain { get; }

    public static MixInput FromTrack(WavTrackSet track, TimeSpan startOffset, TimeSpan? endedAt = null, float gain = 1f)
    {
        ArgumentNullException.ThrowIfNull(track);
        return new MixInput(Path.GetFileName(track.Parts[0].Path), () => MediaFoundationDecoder.Open(track), startOffset, endedAt, gain);
    }

    public static MixInput FromFile(string path, TimeSpan startOffset, TimeSpan? endedAt = null, float gain = 1f)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new MixInput(Path.GetFileName(path), () => MediaFoundationDecoder.Open(path), startOffset, endedAt, gain);
    }

    internal DecodedAudio Open() => _open();
}
