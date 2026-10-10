using Vortice.MediaFoundation;

namespace Memento.Video.Gpu;

/// <summary>Starts Media Foundation once per process (it is reference counted and never shut down while the app runs).</summary>
internal static class MediaFoundationRuntime
{
    private static readonly Lazy<bool> Started = new(() =>
    {
        MediaFactory.MFStartup().CheckError();
        return true;
    });

    public static void EnsureStarted() => _ = Started.Value;
}
