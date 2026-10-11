using Memento.Video.Gpu;

namespace Memento.Video.Sources;

/// <summary>Opens the right <see cref="IVideoSource"/> for an id from <see cref="VideoSourceEnumerator"/>.</summary>
public static class VideoSourceFactory
{
    public static IVideoSource Create(VideoDevice device, VideoSourceId id, string name, GraphicsCaptureOptions? options = null) => id.Kind switch
    {
        VideoSourceKind.Display => GraphicsCaptureSource.ForDisplay(device, id, name, options),
        VideoSourceKind.Window => GraphicsCaptureSource.ForWindow(device, id, name, options),
        _ when id.Backend == CameraBackend.DirectShow => DirectShowCameraSource.ForCamera(device, id, name),
        _ => CameraCaptureSource.ForCamera(device, id, name),
    };
}
