using System.Text.Json;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Settings;

namespace Memento.Core.Bridge.Methods;

/// <summary>
/// The 2.0 <c>video</c> settings block (BRIDGE.md "Video (2.0)", Settings snapshot) for <c>settings.get</c> and
/// <c>settings.set</c>: each field present is changed, the others keep their value; <c>defaultDisplayId</c> and
/// <c>defaultCameraId</c> take JSON <c>null</c> to go back to the primary display and the first camera.
/// </summary>
internal static class VideoSettingsBlock
{
    public static VideoSettingsSnapshot ToSnapshot(VideoSettings video)
    {
        ArgumentNullException.ThrowIfNull(video);
        return new VideoSettingsSnapshot(
            video.RecordVideo,
            video.DefaultSource,
            video.Resolution,
            video.FrameRate,
            video.Quality,
            video.DefaultDisplayId,
            video.DefaultCameraId,
            video.PipCorner,
            video.HardwareEncoder,
            video.IncludePointer);
    }

    /// <exception cref="BridgeException"><c>settings.invalidValue</c> for an id of the wrong kind.</exception>
    public static VideoSettings Merge(VideoSettings current, VideoSettingsPatch patch)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(patch);
        return current with
        {
            RecordVideo = patch.RecordVideo ?? current.RecordVideo,
            DefaultSource = patch.DefaultSource ?? current.DefaultSource,
            Resolution = patch.Resolution ?? current.Resolution,
            FrameRate = patch.FrameRate ?? current.FrameRate,
            Quality = patch.Quality ?? current.Quality,
            DefaultDisplayId = Id(patch.DefaultDisplayId, current.DefaultDisplayId, "video.defaultDisplayId"),
            DefaultCameraId = Id(patch.DefaultCameraId, current.DefaultCameraId, "video.defaultCameraId"),
            PipCorner = patch.PipCorner ?? current.PipCorner,
            HardwareEncoder = patch.HardwareEncoder ?? current.HardwareEncoder,
            IncludePointer = patch.IncludePointer ?? current.IncludePointer,
        };
    }

    private static string? Id(JsonElement element, string? current, string field) => element.ValueKind switch
    {
        JsonValueKind.Undefined => current,
        JsonValueKind.Null => null,
        JsonValueKind.String => string.IsNullOrWhiteSpace(element.GetString()) ? null : element.GetString(),
        _ => throw new BridgeException(DomainErrorCodes.SettingsInvalidValue, $"{field} must be a source id from video.listSources, or null. Nothing was changed.", field),
    };
}
