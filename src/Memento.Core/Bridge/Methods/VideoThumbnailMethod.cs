using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Video;

namespace Memento.Core.Bridge.Methods;

/// <summary>
/// <c>video.thumbnail</c> (2.0): one small still of a display or window (or a camera) for the picker tile and the
/// 72 × 42 row thumbnail, as a data URL. It is never written to disk and never sent anywhere.
/// </summary>
public sealed class VideoThumbnailMethod(IVideoCapture capture) : BridgeMethod<VideoThumbnailParams, VideoThumbnailResult>
{
    public const int DefaultWidth = 320;
    public const int MinWidth = 16;
    public const int MaxWidth = 640;

    public override string Name => BridgeMethodNames.VideoThumbnail;

    public override JsonTypeInfo<VideoThumbnailParams> ParamsTypeInfo => VideoBridgeJsonContext.Default.VideoThumbnailParams;

    public override JsonTypeInfo<VideoThumbnailResult> ResultTypeInfo => VideoBridgeJsonContext.Default.VideoThumbnailResult;

    public override async Task<VideoThumbnailResult> InvokeAsync(VideoThumbnailParams parameters, CancellationToken cancellationToken)
    {
        var width = parameters.Width ?? DefaultWidth;
        if (width is < MinWidth or > MaxWidth)
        {
            throw new BridgeException(BridgeErrorCodes.InvalidParams, $"A thumbnail is {MinWidth} to {MaxWidth} pixels wide, not {width}.");
        }

        if (!VideoSourceIds.IsVideo(parameters.SourceId))
        {
            throw new BridgeException(BridgeErrorCodes.InvalidParams, "sourceId must be a display, window or camera id from video.listSources.", parameters.SourceId);
        }

        try
        {
            return await capture.ThumbnailAsync(parameters.SourceId, width, cancellationToken);
        }
        catch (VideoStartException ex)
        {
            throw new BridgeException(ex.Code, ex.Message, parameters.SourceId);
        }
    }
}
