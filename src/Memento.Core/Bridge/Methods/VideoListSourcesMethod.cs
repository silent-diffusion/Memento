using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Video;

namespace Memento.Core.Bridge.Methods;

/// <summary>
/// <c>video.listSources</c> (2.0): displays, windows and cameras, listed without opening anything (no camera light, no
/// capture border). While the page keeps listing, <see cref="VideoSourceWatch"/> sends <c>video.sourcesChanged</c>.
/// </summary>
public sealed class VideoListSourcesMethod(IVideoCapture capture, VideoSourceWatch watch) : BridgeMethod<EmptyParams, VideoSourcesResult>
{
    public override string Name => BridgeMethodNames.VideoListSources;

    public override JsonTypeInfo<EmptyParams> ParamsTypeInfo => VideoBridgeJsonContext.Default.EmptyParams;

    public override JsonTypeInfo<VideoSourcesResult> ResultTypeInfo => VideoBridgeJsonContext.Default.VideoSourcesResult;

    public override Task<VideoSourcesResult> InvokeAsync(EmptyParams parameters, CancellationToken cancellationToken)
    {
        var sources = capture.ListSources();
        if (capture.IsAvailable)
        {
            watch.Listed(sources);
        }

        return Task.FromResult(sources);
    }
}
