using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Transcripts;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>transcript.search</c>: case-insensitive, matches starting at a word boundary.</summary>
public sealed class TranscriptSearchMethod(TranscriptService transcripts) : BridgeMethod<TranscriptSearchParams, TranscriptSearchResult>
{
    public override string Name => BridgeMethodNames.TranscriptSearch;

    public override JsonTypeInfo<TranscriptSearchParams> ParamsTypeInfo => BridgeJsonContext.Default.TranscriptSearchParams;

    public override JsonTypeInfo<TranscriptSearchResult> ResultTypeInfo => BridgeJsonContext.Default.TranscriptSearchResult;

    public override async Task<TranscriptSearchResult> InvokeAsync(TranscriptSearchParams parameters, CancellationToken cancellationToken) =>
        new(await transcripts.SearchAsync(parameters.RecordingId, parameters.Query, cancellationToken));
}
