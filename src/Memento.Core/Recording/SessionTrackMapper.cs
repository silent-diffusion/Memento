using Memento.Core.Audio;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Projects;

namespace Memento.Core.Recording;

/// <summary>Converts engine tracks to the manifest, the recovery state file and the bridge.</summary>
public static class SessionTrackMapper
{
    public static Track ToContract(SessionTrack track)
    {
        ArgumentNullException.ThrowIfNull(track);
        return new Track(
            track.TrackId,
            track.Source.Id,
            track.Source.Kind,
            track.Source.Name,
            track.File,
            track.Format.SampleRate,
            track.Format.Channels,
            track.DurationMs,
            null,
            track.EndedAtMs);
    }

    public static ProjectTrack ToManifest(SessionTrack track)
    {
        ArgumentNullException.ThrowIfNull(track);
        return new ProjectTrack
        {
            Id = track.TrackId,
            SourceId = track.Source.Id,
            SourceKind = track.Source.Kind,
            Name = track.Source.Name,
            File = track.File,
            CaptureFile = track.File,
            SampleRate = track.Format.SampleRate,
            Channels = track.Format.Channels,
            BitsPerSample = track.Format.BitsPerSample,
            SampleEncoding = EncodingName(track.Format.Encoding),
            Codec = PassThroughWavEncoder.WavCodec,
            StartOffsetMs = track.StartOffsetMs,
            DurationMs = track.DurationMs,
            EndedEarlyAtMs = track.EndedAtMs,
            EndReason = ReasonName(track.EndReason),
        };
    }

    public static RecordingStateTrack ToState(SessionTrack track)
    {
        ArgumentNullException.ThrowIfNull(track);
        return new RecordingStateTrack(
            track.TrackId,
            track.Source.Id,
            track.Source.Kind,
            track.Source.Name,
            track.File,
            track.Format.SampleRate,
            track.Format.Channels,
            track.Format.BitsPerSample,
            EncodingName(track.Format.Encoding),
            track.StartOffsetMs,
            track.CheckpointedBytes,
            track.EndedAtMs,
            ReasonName(track.EndReason));
    }

    public static PcmFormat FormatOf(RecordingStateTrack track)
    {
        ArgumentNullException.ThrowIfNull(track);
        return new PcmFormat(track.SampleRate, track.Channels, track.BitsPerSample, track.SampleEncoding == "float" ? SampleEncoding.IeeeFloat : SampleEncoding.Pcm);
    }

    public static string EncodingName(SampleEncoding encoding) => encoding == SampleEncoding.IeeeFloat ? "float" : "pcm";

    public static string? ReasonName(TrackEndReason? reason) => reason switch
    {
        TrackEndReason.Disabled => "disabled",
        TrackEndReason.SourceLost => "sourceLost",
        TrackEndReason.DiskFull => "diskFull",
        TrackEndReason.Error => "error",
        _ => null,
    };
}
