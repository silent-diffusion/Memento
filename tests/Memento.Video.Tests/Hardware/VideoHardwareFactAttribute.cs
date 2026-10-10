using Memento.Video.Sources;

namespace Memento.Video.Tests.Hardware;

/// <summary>
/// A fact that records real video. Skipped on CI (no interactive desktop, no graphics encoder) and wherever
/// Windows.Graphics.Capture is unavailable; <paramref name="needsCamera"/> also skips when no camera is connected.
/// Run them with <c>dotnet test --filter Category=Hardware</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class VideoHardwareFactAttribute : FactAttribute
{
    public VideoHardwareFactAttribute(bool needsCamera = false)
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")))
        {
            Skip = "Video hardware tests do not run on CI.";
        }
        else if (!GraphicsCaptureSource.IsSupported())
        {
            Skip = "Windows.Graphics.Capture is not available here.";
        }
        else if (needsCamera && VideoSourceEnumerator.ListCameras().Count == 0)
        {
            Skip = "No camera is connected.";
        }
    }
}
