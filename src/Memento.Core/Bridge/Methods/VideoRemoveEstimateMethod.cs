using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Settings;
using Memento.Core.Video;

namespace Memento.Core.Bridge.Methods;

/// <summary>
/// <c>video.removeEstimate</c> (2.0): what Remove video would free: the named recordings, or every recording with video
/// older than <c>olderThanDays</c> (null: every one). Feeds Settings' "Would free 11.3 GB now, from 3 recordings" and the
/// Remove video dialog. Reads only.
/// </summary>
public sealed class VideoRemoveEstimateMethod(VideoRemovalService removal) : BridgeMethod<VideoRemoveEstimateParams, VideoRemoveEstimate>
{
    public override string Name => BridgeMethodNames.VideoRemoveEstimate;

    public override JsonTypeInfo<VideoRemoveEstimateParams> ParamsTypeInfo => VideoBridgeJsonContext.Default.VideoRemoveEstimateParams;

    public override JsonTypeInfo<VideoRemoveEstimate> ResultTypeInfo => VideoBridgeJsonContext.Default.VideoRemoveEstimate;

    public override async Task<VideoRemoveEstimate> InvokeAsync(VideoRemoveEstimateParams parameters, CancellationToken cancellationToken)
    {
        if (parameters.OlderThanDays is < LibraryStorageSettings.MinDays or > LibraryStorageSettings.MaxDays)
        {
            throw new BridgeException(BridgeErrorCodes.InvalidParams, $"olderThanDays is {LibraryStorageSettings.MinDays} to {LibraryStorageSettings.MaxDays}, or null.");
        }

        var (recordings, bytes) = await removal.EstimateAsync(parameters.RecordingIds, parameters.OlderThanDays, cancellationToken);
        return new VideoRemoveEstimate(recordings, bytes);
    }
}
