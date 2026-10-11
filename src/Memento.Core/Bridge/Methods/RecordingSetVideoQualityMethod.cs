using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Recording;

namespace Memento.Core.Bridge.Methods;

/// <summary>
/// <c>recording.setVideoQuality</c> (2.0): the §18.6 encoder banner's Lower quality. The running video tracks continue at
/// the new quality's bitrate (a new part of the same track, so nothing recorded is lost).
/// </summary>
public sealed class RecordingSetVideoQualityMethod(RecordingCoordinator recordings) : BridgeMethod<RecordingSetVideoQualityParams, VideoTracksResult>
{
    public override string Name => BridgeMethodNames.RecordingSetVideoQuality;

    public override JsonTypeInfo<RecordingSetVideoQualityParams> ParamsTypeInfo => VideoBridgeJsonContext.Default.RecordingSetVideoQualityParams;

    public override JsonTypeInfo<VideoTracksResult> ResultTypeInfo => VideoBridgeJsonContext.Default.VideoTracksResult;

    public override async Task<VideoTracksResult> InvokeAsync(RecordingSetVideoQualityParams parameters, CancellationToken cancellationToken) =>
        new(await recordings.SetVideoQualityAsync(parameters.SessionId, parameters.Quality, cancellationToken));
}
