namespace Memento.Audio.Capture;

/// <summary>Tuning for <see cref="WasapiAudioCapture"/>. The defaults are the values verified in the spikes.</summary>
public sealed record CaptureOptions
{
    public static CaptureOptions Default { get; } = new();

    /// <summary>Packets the channel holds before the producer starts dropping (about 10 s at 10 ms packets).</summary>
    public int ChannelCapacity { get; init; } = 1024;

    /// <summary>Shared-mode buffer for endpoint capture and endpoint loopback.</summary>
    public TimeSpan EndpointBufferDuration { get; init; } = TimeSpan.FromMilliseconds(100);

    /// <summary>Buffer for process loopback (ENGINE-NOTES.md §C: 20 ms).</summary>
    public TimeSpan ProcessLoopbackBufferDuration { get; init; } = TimeSpan.FromMilliseconds(20);

    /// <summary>Process loopback has no mix format; the caller picks one and the engine converts.</summary>
    public AudioFormat ProcessLoopbackFormat { get; init; } = AudioFormat.Float32Stereo48k;

    /// <summary>Insert clock-timed silence for endpoint loopback (on by default; it delivers nothing while silent).</summary>
    public bool FillSilenceForSystemLoopback { get; init; } = true;

    /// <summary>Process loopback streams zeros by itself; filling is harmless and covers any engine hiccup.</summary>
    public bool FillSilenceForProcessLoopback { get; init; } = true;

    public TimeSpan SilenceHoldback { get; init; } = TimeSpan.FromMilliseconds(100);

    public TimeSpan GapThreshold { get; init; } = TimeSpan.FromMilliseconds(5);

    /// <summary>How long process-loopback activation may take before the source is reported unavailable.</summary>
    public TimeSpan ActivationTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>MMCSS tasks tried in order for the capture thread.</summary>
    public IReadOnlyList<string> MmcssTasks { get; init; } = ["Pro Audio", "Capture"];
}
