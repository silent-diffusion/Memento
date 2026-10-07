namespace Memento.AI;

/// <summary>What a provider can do, so the pipeline can budget chunks and pick an output constraint.</summary>
/// <param name="MaxContextTokens">Prompt plus output tokens one request may use.</param>
/// <param name="MaxOutputTokens">The most output tokens one request may ask for.</param>
/// <param name="SupportsJsonSchema">Output can be constrained by <see cref="AiRequest.JsonSchema"/>.</param>
/// <param name="SupportsGrammar">Output can be constrained by a GBNF grammar (<see cref="AiRequest.Grammar"/>).</param>
/// <param name="SupportsStreaming">Text arrives as it is generated (<see cref="AiProgress.Delta"/>).</param>
/// <param name="ExactTokenCounts"><see cref="IAiProvider.CountTokens"/> uses the model's own tokenizer rather than an estimate.</param>
public sealed record AiCapabilities(
    int MaxContextTokens,
    int MaxOutputTokens,
    bool SupportsJsonSchema,
    bool SupportsGrammar,
    bool SupportsStreaming,
    bool ExactTokenCounts);
