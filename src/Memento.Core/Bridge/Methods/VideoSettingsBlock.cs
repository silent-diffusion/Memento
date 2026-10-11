using Memento.Core.Bridge.Contracts;
using Memento.Core.Settings;

namespace Memento.Core.Bridge.Methods;

/// <summary>
/// <c>recording.video</c> (2.0, Settings › Recording › Video defaults) for <c>settings.get</c> and <c>settings.set</c>. It is
/// part of the <c>recording</c> block, which is replaced whole: a field left out or <c>null</c> takes its default.
/// </summary>
internal static class VideoSettingsBlock
{
    public static VideoSettingsSnapshot ToSnapshot(VideoSettings video)
    {
        ArgumentNullException.ThrowIfNull(video);
        return new VideoSettingsSnapshot(
            video.DefaultSource,
            video.Resolution,
            video.FrameRate,
            video.Quality,
            video.DefaultCameraId,
            video.PipCorner,
            video.HardwareEncoder,
            video.IncludePointer);
    }

    /// <summary>The block as sent, defaults for what it leaves out; unknown fields of <paramref name="current"/> survive.</summary>
    public static VideoSettings Replace(VideoSettings current, VideoSettingsPatch? patch)
    {
        ArgumentNullException.ThrowIfNull(current);
        var defaults = new VideoSettings { ExtensionData = current.ExtensionData };
        if (patch is null)
        {
            return defaults;
        }

        return defaults with
        {
            DefaultSource = patch.DefaultSource ?? defaults.DefaultSource,
            Resolution = patch.Resolution ?? defaults.Resolution,
            FrameRate = patch.FrameRate ?? defaults.FrameRate,
            Quality = patch.Quality ?? defaults.Quality,
            DefaultCameraId = string.IsNullOrWhiteSpace(patch.DefaultCameraId) ? null : patch.DefaultCameraId,
            PipCorner = patch.PipCorner ?? defaults.PipCorner,
            HardwareEncoder = patch.HardwareEncoder ?? defaults.HardwareEncoder,
            IncludePointer = patch.IncludePointer ?? defaults.IncludePointer,
        };
    }
}
