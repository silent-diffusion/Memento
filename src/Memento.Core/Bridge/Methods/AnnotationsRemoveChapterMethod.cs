using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Projects;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>annotations.removeChapter</c>.</summary>
public sealed class AnnotationsRemoveChapterMethod(ProjectService projects) : BridgeMethod<ChapterIdParams, ChaptersResult>
{
    public override string Name => BridgeMethodNames.AnnotationsRemoveChapter;

    public override JsonTypeInfo<ChapterIdParams> ParamsTypeInfo => BridgeJsonContext.Default.ChapterIdParams;

    public override JsonTypeInfo<ChaptersResult> ResultTypeInfo => BridgeJsonContext.Default.ChaptersResult;

    public override async Task<ChaptersResult> InvokeAsync(ChapterIdParams parameters, CancellationToken cancellationToken)
    {
        return new ChaptersResult(await projects.RemoveChapterAsync(parameters.RecordingId, parameters.ChapterId, cancellationToken));
    }
}
