namespace Memento.Video.Recovery;

/// <summary>One complete fragment of a fragmented MP4: <c>moof</c> + <c>mdat</c>.</summary>
/// <param name="Offset">Where its <c>moof</c> starts.</param>
/// <param name="Length">Bytes of <c>moof</c> and <c>mdat</c> together.</param>
/// <param name="DecodeTime">Its first sample's decode time (<c>tfdt</c>), in the track's timescale.</param>
/// <param name="Duration">Sum of its sample durations, in the track's timescale.</param>
public sealed record Mp4Fragment(long Offset, long Length, long DecodeTime, long Duration, uint Samples);
