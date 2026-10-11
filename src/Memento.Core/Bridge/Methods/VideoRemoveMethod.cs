using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Projects;
using Memento.Core.Video;

namespace Memento.Core.Bridge.Methods;

/// <summary>
/// <c>video.remove</c> (2.0): the Remove video dialog's action (DESIGN.md §18.6, after the page's confirmation).
/// Deletes the recording's video files, keeps audio, transcript and documents, and writes History.
/// </summary>
public sealed class VideoRemoveMethod(VideoRemovalService removal) : BridgeMethod<RecordingIdParams, VideoRemoveResult>
{
    public override string Name => BridgeMethodNames.VideoRemove;

    public override JsonTypeInfo<RecordingIdParams> ParamsTypeInfo => VideoBridgeJsonContext.Default.RecordingIdParams;

    public override JsonTypeInfo<VideoRemoveResult> ResultTypeInfo => VideoBridgeJsonContext.Default.VideoRemoveResult;

    public override async Task<VideoRemoveResult> InvokeAsync(RecordingIdParams parameters, CancellationToken cancellationToken) =>
        new(await removal.RemoveAsync(parameters.RecordingId, ProjectVideoRemoved.ByUser, cancellationToken));
}
