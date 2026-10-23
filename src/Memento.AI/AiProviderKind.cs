namespace Memento.AI;

/// <summary>
/// What a provider is: another company's service, the on-device model, or a server the user runs. Whether text leaves
/// the PC is not the kind's to say for <see cref="Server"/> (it depends on the address); callers key consent on
/// <c>LeavesPc</c>, never on the kind.
/// </summary>
public enum AiProviderKind
{
    /// <summary>An external service (Anthropic, OpenAI). Text leaves the PC; needs a key and the user's consent.</summary>
    Cloud,

    /// <summary>An on-device model. Nothing leaves the PC; no key.</summary>
    Local,

    /// <summary>
    /// "Your own server" (2.5.0): an OpenAI-compatible server the user runs (Ollama, LM Studio, vLLM, a llama.cpp
    /// server). On this PC nothing leaves it; at any other address text leaves the PC as it does for a cloud provider.
    /// </summary>
    Server,
}
