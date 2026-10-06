using Memento.Audio.Capture;
using Memento.Audio.Sources;
using Memento.Audio.Writing;

namespace Memento.Audio.Adapters;

/// <summary>Seams of <see cref="WasapiRecordingEngine"/>; the defaults record real devices.</summary>
public sealed record WasapiEngineOptions
{
    /// <summary>Opens captures; <c>null</c> for WASAPI. Tests substitute synthetic sources.</summary>
    public IAudioCaptureFactory? CaptureFactory { get; init; }

    /// <summary>Names a source for its track file; <c>null</c> for <see cref="AudioSourceEnumerator.Describe"/>.</summary>
    public Func<AudioSourceId, AudioSourceInfo?>? Describe { get; init; }

    /// <summary>Size at which a track rolls over to its next <c>.partN.wav</c> (3.5 GiB; tests use less).</summary>
    public long RolloverBytes { get; init; } = RollingWavWriter.DefaultRolloverBytes;

    /// <summary><c>FlushFileBuffers</c> at every checkpoint (always on in the app; tests may skip it for speed).</summary>
    public bool DurableCheckpoints { get; init; } = true;
}
