using System.Text.Json;

namespace Memento.AI;

/// <summary>The answer to one <see cref="AiRequest"/>.</summary>
/// <param name="ProviderId"><see cref="IAiProvider.Id"/>.</param>
/// <param name="Model">The model that produced the answer (for a cloud fallback, the fallback model).</param>
/// <param name="Text">The answer text exactly as generated.</param>
/// <param name="Json">The parsed answer when the request asked for JSON and generation completed; otherwise <c>null</c>.</param>
/// <param name="StopReason">Why generation ended; parse only <see cref="AiStopReason.Completed"/> answers.</param>
/// <param name="ProviderStopReason">The provider's own stop reason string (<c>end_turn</c>, <c>max_output_tokens</c>, <c>eog</c>).</param>
/// <param name="RequestHash">SHA-256 of the canonical request (<see cref="AiRequestHash"/>), for the generation record.</param>
/// <param name="FellBack">A cloud provider's safety fallback served the answer with another model.</param>
public sealed record AiResponse(
    string ProviderId,
    string Model,
    string Text,
    JsonElement? Json,
    AiStopReason StopReason,
    string? ProviderStopReason,
    AiUsage Usage,
    AiTimings Timings,
    string RequestHash,
    bool FellBack = false);
