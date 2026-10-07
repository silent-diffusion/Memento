using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Transcripts;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>transcript.markReviewed</c>.</summary>
public sealed class TranscriptMarkReviewedMethod(TranscriptService transcripts) : BridgeMethod<TranscriptMarkReviewedParams, MarkReviewedResult>
{
    public override string Name => BridgeMethodNames.TranscriptMarkReviewed;

    public override JsonTypeInfo<TranscriptMarkReviewedParams> ParamsTypeInfo => BridgeJsonContext.Default.TranscriptMarkReviewedParams;

    public override JsonTypeInfo<MarkReviewedResult> ResultTypeInfo => BridgeJsonContext.Default.MarkReviewedResult;

    public override async Task<MarkReviewedResult> InvokeAsync(TranscriptMarkReviewedParams parameters, CancellationToken cancellationToken) =>
        new(await transcripts.MarkReviewedAsync(parameters.RecordingId, parameters.Reviewed, cancellationToken));
}
