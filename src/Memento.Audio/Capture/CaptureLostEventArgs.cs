namespace Memento.Audio.Capture;

/// <summary>A capture stream ended because its device, session or process went away.</summary>
public sealed class CaptureLostEventArgs(AudioSourceId source, CaptureLostReason reason, int hResult, long lostAtQpc) : EventArgs
{
    public AudioSourceId Source { get; } = source;

    public CaptureLostReason Reason { get; } = reason;

    /// <summary>The failing HRESULT, or 0 when the loss came from a notification.</summary>
    public int HResult { get; } = hResult;

    /// <summary>When the loss was detected, in <see cref="QpcClock"/> ticks; the track ends here.</summary>
    public long LostAtQpc { get; } = lostAtQpc;
}
