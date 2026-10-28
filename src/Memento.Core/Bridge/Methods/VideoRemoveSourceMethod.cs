using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Recording;

namespace Memento.Core.Bridge.Methods;

/// <summary>
/// <c>video.removeSource</c> (2.8.0): stops one video track from now while the recording runs; what it recorded is kept.
/// Removing the last screen is allowed: the audio keeps recording.
/// </summary>
public sealed class VideoRemoveSourceMethod(RecordingCoordinator recordings) : BridgeMethod<VideoRemoveSourceParams, VideoTracksResult>
{
    public override string Name => BridgeMethodNames.VideoRemoveSource;

    public override JsonTypeInfo<VideoRemoveSourceParams> ParamsTypeInfo => VideoBridgeJsonContext.Default.VideoRemoveSourceParams;

    public override JsonTypeInfo<VideoTracksResult> ResultTypeInfo => VideoBridgeJsonContext.Default.VideoTracksResult;

    public override async Task<VideoTracksResult> InvokeAsync(VideoRemoveSourceParams parameters, CancellationToken cancellationToken) =>
        new(await recordings.RemoveVideoSourceAsync(parameters.SessionId, parameters.TrackId, cancellationToken));
}
