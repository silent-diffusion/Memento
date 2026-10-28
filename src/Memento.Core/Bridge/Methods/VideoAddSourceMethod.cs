using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Recording;

namespace Memento.Core.Bridge.Methods;

/// <summary>
/// <c>video.addSource</c> (2.8.0): records one more display or window from now while the recording runs. The new track
/// has its own file and time range; audio and the other tracks are never touched.
/// </summary>
public sealed class VideoAddSourceMethod(RecordingCoordinator recordings) : BridgeMethod<VideoAddSourceParams, VideoTracksResult>
{
    public override string Name => BridgeMethodNames.VideoAddSource;

    public override JsonTypeInfo<VideoAddSourceParams> ParamsTypeInfo => VideoBridgeJsonContext.Default.VideoAddSourceParams;

    public override JsonTypeInfo<VideoTracksResult> ResultTypeInfo => VideoBridgeJsonContext.Default.VideoTracksResult;

    public override async Task<VideoTracksResult> InvokeAsync(VideoAddSourceParams parameters, CancellationToken cancellationToken) =>
        new(await recordings.AddVideoSourceAsync(parameters.SessionId, parameters.SourceId, cancellationToken));
}
