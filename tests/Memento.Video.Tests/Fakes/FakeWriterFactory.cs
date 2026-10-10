using Memento.Video.Encoding;

namespace Memento.Video.Tests.Fakes;

/// <summary>Hands out <see cref="FakeTrackWriter"/>s: hardware when the settings ask for it, unless told there is none.</summary>
internal sealed class FakeWriterFactory : IVideoTrackWriterFactory
{
    public List<FakeTrackWriter> Writers { get; } = [];

    public bool HardwareAvailable { get; set; } = true;

    public bool NoEncoderAtAll { get; set; }

    public IVideoTrackWriter Create(string path, VideoEncoderSettings settings)
    {
        if (NoEncoderAtAll)
        {
            throw new VideoCaptureException(VideoErrorCode.EncoderUnavailable, VideoErrors.NoEncoder());
        }

        var hardware = settings.PreferHardware && HardwareAvailable;
        var writer = new FakeTrackWriter(path, settings, hardware
            ? new H264EncoderInfo("Test hardware encoder", true, 1, "VEN_TEST")
            : new H264EncoderInfo("Microsoft H.264 software encoder", false, null, null));
        Writers.Add(writer);
        return writer;
    }
}
