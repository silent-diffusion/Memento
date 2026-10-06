using System.Threading.Channels;

namespace Memento.Audio.Capture;

/// <summary>
/// An opened, initialised capture source. Packets flow through <see cref="Packets"/>; the producer never waits
/// on the consumer (a full channel drops the packet and counts an overrun). The channel completes when the
/// stream stops or is lost.
/// </summary>
public interface IAudioCapture : IAsyncDisposable
{
    AudioSourceId Source { get; }

    /// <summary>The format packets are delivered in (the endpoint mix format, or float32 48 kHz stereo for process loopback).</summary>
    AudioFormat Format { get; }

    ChannelReader<CapturePacket> Packets { get; }

    CaptureStatistics Statistics { get; }

    /// <summary>QPC time when capture started (0 before <see cref="Start"/>).</summary>
    long StartedAtQpc { get; }

    /// <summary>Set (before the channel completes) if the source went away; null after a normal stop.</summary>
    CaptureLostEventArgs? Loss { get; }

    /// <summary>Raised once, on a thread-pool thread, if the source goes away. The channel completes right after.</summary>
    event EventHandler<CaptureLostEventArgs>? Lost;

    void Start();

    /// <summary>Stops capture, drains what WASAPI still holds into the channel and completes it.</summary>
    Task StopAsync();
}
