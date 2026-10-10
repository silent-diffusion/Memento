using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Transcripts;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>annotations.suggestChapters</c> (2.0): chapters suggested on this PC from topic shifts, pauses and speaker turns.</summary>
public sealed class AnnotationsSuggestChaptersMethod(ChapterSuggestionService suggestions) : BridgeMethod<RecordingIdParams, ChapterSuggestionsResult>
{
    public override string Name => BridgeMethodNames.AnnotationsSuggestChapters;

    public override JsonTypeInfo<RecordingIdParams> ParamsTypeInfo => ReviewBridgeJsonContext.Default.RecordingIdParams;

    public override JsonTypeInfo<ChapterSuggestionsResult> ResultTypeInfo => ReviewBridgeJsonContext.Default.ChapterSuggestionsResult;

    public override async Task<ChapterSuggestionsResult> InvokeAsync(RecordingIdParams parameters, CancellationToken cancellationToken) =>
        new ChapterSuggestionsResult(await suggestions.SuggestAsync(parameters.RecordingId, cancellationToken));
}
