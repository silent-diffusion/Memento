namespace Memento.AI.Http;

/// <summary>What a provider's stream reader extracted from one successful response.</summary>
internal sealed record CloudStreamResult(
    string Text,
    string? Model,
    AiStopReason StopReason,
    string? ProviderStopReason,
    AiUsage Usage,
    bool FellBack = false);
