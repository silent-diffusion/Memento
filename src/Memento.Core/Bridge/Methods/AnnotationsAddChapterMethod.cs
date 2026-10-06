using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Projects;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>annotations.addChapter</c>.</summary>
public sealed class AnnotationsAddChapterMethod(ProjectService projects) : BridgeMethod<ChapterParams, ChaptersResult>
{
    public override string Name => BridgeMethodNames.AnnotationsAddChapter;

    public override JsonTypeInfo<ChapterParams> ParamsTypeInfo => BridgeJsonContext.Default.ChapterParams;

    public override JsonTypeInfo<ChaptersResult> ResultTypeInfo => BridgeJsonContext.Default.ChaptersResult;

    public override async Task<ChaptersResult> InvokeAsync(ChapterParams parameters, CancellationToken cancellationToken)
    {
        return new ChaptersResult(await projects.AddChapterAsync(parameters.RecordingId, parameters.Chapter, cancellationToken));
    }
}
