namespace Memento.Audio.Capture;

/// <summary>Per-packet flags: the WASAPI buffer flags plus what our capture loop adds.</summary>
[Flags]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Mirrors AUDCLNT_BUFFERFLAGS.")]
public enum CapturePacketFlags
{
    None = 0,

    /// <summary><c>AUDCLNT_BUFFERFLAGS_DATA_DISCONTINUITY</c>: the engine dropped data before this packet.</summary>
    Discontinuity = 0x1,

    /// <summary><c>AUDCLNT_BUFFERFLAGS_SILENT</c>, or synthesized: treat the packet as digital silence (no data bytes).</summary>
    Silent = 0x2,

    /// <summary><c>AUDCLNT_BUFFERFLAGS_TIMESTAMP_ERROR</c>: the QPC position is not reliable.</summary>
    TimestampError = 0x4,

    /// <summary>Clock-timed silence inserted by the capture loop while an endpoint loopback delivered nothing.</summary>
    Synthesized = 0x100,

    /// <summary>The consumer fell behind and packets before this one were dropped (<see cref="CapturePacket.DroppedFramesBefore"/>).</summary>
    AfterOverrun = 0x200,
}
