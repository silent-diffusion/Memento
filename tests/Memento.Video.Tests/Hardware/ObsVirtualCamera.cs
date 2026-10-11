using Memento.Video.Sources;

namespace Memento.Video.Tests.Hardware;

/// <summary>The OBS Virtual Camera as Memento lists it, if it is installed.</summary>
internal static class ObsVirtualCamera
{
    public const string Name = "OBS Virtual Camera";

    public static VideoSourceInfo? Find() =>
        VideoSourceEnumerator.ListCameras().FirstOrDefault(c => c.Backend == CameraBackend.DirectShow && c.Name == Name);
}
