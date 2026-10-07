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
/// <param name="BatchVerify">Ask a cloud model to verify many claims per request; the local model answers one at a time.</param>
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
    bool BatchVerify)
{
    /// <summary>Sees every request and its answer (tests and diagnostics; the text is content and is never logged).</summary>
    public Action<AiRequest, AiResponse>? OnResponse { get; init; }
}
