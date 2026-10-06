using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Projects;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>annotations.updateChapter</c>: <c>chapter.id</c> names the chapter; other fields are a partial update.</summary>
public sealed class AnnotationsUpdateChapterMethod(ProjectService projects) : BridgeMethod<ChapterParams, ChaptersResult>
{
    public override string Name => BridgeMethodNames.AnnotationsUpdateChapter;

    public override JsonTypeInfo<ChapterParams> ParamsTypeInfo => BridgeJsonContext.Default.ChapterParams;

    public override JsonTypeInfo<ChaptersResult> ResultTypeInfo => BridgeJsonContext.Default.ChaptersResult;

    public override async Task<ChaptersResult> InvokeAsync(ChapterParams parameters, CancellationToken cancellationToken)
    {
        return new ChaptersResult(await projects.UpdateChapterAsync(parameters.RecordingId, parameters.Chapter, cancellationToken));
    }
}
