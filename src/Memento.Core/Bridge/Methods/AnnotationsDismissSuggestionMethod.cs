using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Transcripts;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>annotations.dismissSuggestion</c> (2.0): dismisses a suggested chapter for good (Undo: <c>annotations.restoreSuggestion</c>).</summary>
public sealed class AnnotationsDismissSuggestionMethod(ChapterSuggestionService suggestions) : BridgeMethod<ChapterSuggestionParams, ChapterSuggestionsResult>
{
    public override string Name => BridgeMethodNames.AnnotationsDismissSuggestion;

    public override JsonTypeInfo<ChapterSuggestionParams> ParamsTypeInfo => ReviewBridgeJsonContext.Default.ChapterSuggestionParams;

    public override JsonTypeInfo<ChapterSuggestionsResult> ResultTypeInfo => ReviewBridgeJsonContext.Default.ChapterSuggestionsResult;

    public override async Task<ChapterSuggestionsResult> InvokeAsync(ChapterSuggestionParams parameters, CancellationToken cancellationToken) =>
        new ChapterSuggestionsResult(await suggestions.DismissAsync(parameters.RecordingId, parameters.AtMs, cancellationToken));
}
