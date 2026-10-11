using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Recording;

namespace Memento.Core.Bridge.Methods;

/// <summary>
/// <c>recording.setVideoSource</c> (2.0): starts the role's video track, switches it to another source (the track
/// continues, letterboxed), or with <c>sourceId: null</c> ends it. Audio tracks are never touched.
/// </summary>
public sealed class RecordingSetVideoSourceMethod(RecordingCoordinator recordings) : BridgeMethod<RecordingSetVideoSourceParams, VideoTracksResult>
{
    public override string Name => BridgeMethodNames.RecordingSetVideoSource;

    public override JsonTypeInfo<RecordingSetVideoSourceParams> ParamsTypeInfo => VideoBridgeJsonContext.Default.RecordingSetVideoSourceParams;

    public override JsonTypeInfo<VideoTracksResult> ResultTypeInfo => VideoBridgeJsonContext.Default.VideoTracksResult;

    public override async Task<VideoTracksResult> InvokeAsync(RecordingSetVideoSourceParams parameters, CancellationToken cancellationToken) =>
        new(await recordings.SetVideoSourceAsync(parameters.SessionId, parameters.Role, parameters.SourceId, cancellationToken));
}
