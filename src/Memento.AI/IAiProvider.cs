namespace Memento.AI;

/// <summary>
/// A text generation provider (ARCHITECTURE.md section 8): Anthropic, OpenAI or the local model. Implementations are
/// thread-safe; the local one serialises requests per model context. Nothing here logs prompt or answer text, and a
/// cloud provider sends exactly the request it is given, once (retries happen only when the provider answered with a
/// rate limit or a temporary server error before producing anything).
/// </summary>
public interface IAiProvider
{
    /// <summary>Stable id: <c>anthropic</c>, <c>openai</c>, <c>local</c>.</summary>
    string Id { get; }

    /// <summary>The name the interface uses: "Claude", "ChatGPT", "Local model".</summary>
    string DisplayName { get; }

    AiProviderKind Kind { get; }

    /// <summary>The model requests go to (cloud model id, or the local catalog id).</summary>
    string Model { get; }

    AiCapabilities Capabilities { get; }

    /// <summary>
    /// Tokens <paramref name="text"/> takes in this provider's prompt: exact for the local model (its tokenizer), a
    /// calibrated estimate that errs high for cloud providers (counting remotely would send the text).
    /// </summary>
    int CountTokens(string text);

    /// <summary>Generates one answer.</summary>
    /// <exception cref="AiException">Any failure, with one of <see cref="AiErrorCodes"/>.</exception>
    /// <exception cref="OperationCanceledException">Cancelled.</exception>
    Task<AiResponse> GenerateAsync(AiRequest request, IProgress<AiProgress>? progress, CancellationToken cancellationToken);

    /// <summary>Whether a generation could start now (key saved, model installed, enough video memory). Sends nothing.</summary>
    Task<AiReadiness> CheckAsync(CancellationToken cancellationToken);
}
