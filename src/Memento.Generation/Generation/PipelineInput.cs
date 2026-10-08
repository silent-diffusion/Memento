using Memento.AI;
using Memento.AI.Payload;
using Memento.Documents.Templates;
using Memento.Generation.Documents;

namespace Memento.Generation.Generation;

/// <summary>One generation: the template, the recording's material, the payload composed from the allowed inputs, the
/// provider, and the budgets for this provider.</summary>
/// <param name="ChunkTokens">Transcript tokens per chunk (Local: from the model profile and the context it got; cloud: larger).</param>
/// <param name="MapOutputTokens">The answer limit of a map request.</param>
/// <param name="Bounded">Item limits in the map schemas (the local grammar); cloud structured outputs get plain schemas.</param>
/// <param name="VerifyBatch">Verification questions per request: <see cref="GenerationPipeline.VerifyBatchSize"/> for a cloud model, <see cref="GenerationPipeline.LocalVerifyBatchSize"/> per module family for the local model; 1 asks one at a time.</param>
public sealed record PipelineInput(
    DocumentTemplate Template,
    RecordingMaterial Material,
    ComposedPayload Payload,
    PayloadSelection Selection,
    IAiProvider Provider,
    GenerationFacts Facts,
    int ChunkTokens,
    int MapOutputTokens,
    bool Bounded,
    int VerifyBatch)
{
    /// <summary>Sees every request and its answer (tests and diagnostics; the text is content and is never logged).</summary>
    public Action<AiRequest, AiResponse>? OnResponse { get; init; }

    /// <summary>The Live output sheet's feed: every request, the local model's tokens, every reply and the steps done in code.</summary>
    public GenerationOutputFeed? Output { get; init; }
}
