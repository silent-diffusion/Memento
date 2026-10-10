using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Transcripts;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>annotations.restoreSuggestion</c> (2.0): suggests a dismissed chapter again (the Undo of a dismiss).</summary>
public sealed class AnnotationsRestoreSuggestionMethod(ChapterSuggestionService suggestions) : BridgeMethod<ChapterSuggestionParams, ChapterSuggestionsResult>
{
    public override string Name => BridgeMethodNames.AnnotationsRestoreSuggestion;

    public override JsonTypeInfo<ChapterSuggestionParams> ParamsTypeInfo => ReviewBridgeJsonContext.Default.ChapterSuggestionParams;

    public override JsonTypeInfo<ChapterSuggestionsResult> ResultTypeInfo => ReviewBridgeJsonContext.Default.ChapterSuggestionsResult;

    public override async Task<ChapterSuggestionsResult> InvokeAsync(ChapterSuggestionParams parameters, CancellationToken cancellationToken) =>
        new ChapterSuggestionsResult(await suggestions.RestoreAsync(parameters.RecordingId, parameters.AtMs, cancellationToken));
}
