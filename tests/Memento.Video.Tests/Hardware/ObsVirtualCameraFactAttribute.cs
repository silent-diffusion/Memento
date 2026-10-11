using Memento.Video.Sources;

namespace Memento.Video.Tests.Hardware;

/// <summary>
/// A fact that records the OBS Virtual Camera (ENGINE-NOTES.md §R). Skipped on CI, where OBS is not installed, and
/// wherever OBS's virtual camera is not running (start it in OBS, or <c>obs64 --startvirtualcam</c>). The tests never
/// start or stop OBS themselves. Run them with <c>dotnet test --filter Category=Hardware</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ObsVirtualCameraFactAttribute : FactAttribute
{
    public ObsVirtualCameraFactAttribute()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")))
        {
            Skip = "Video hardware tests do not run on CI.";
        }
        else if (ObsVirtualCamera.Find() is not { } camera)
        {
            Skip = "The OBS Virtual Camera is not installed.";
        }
        else if (VirtualCameraWatch.For(camera.Id) is not { } watch || !watch.IsSending())
        {
            Skip = "The OBS Virtual Camera is not running (Start Virtual Camera in OBS).";
        }
    }
}
