namespace Memento.AI;

/// <summary>Where a provider runs: on another company's servers, or on this PC.</summary>
public enum AiProviderKind
{
    /// <summary>An external service (Anthropic, OpenAI). Text leaves the PC; needs a key and the user's consent.</summary>
    Cloud,

    /// <summary>An on-device model. Nothing leaves the PC; no key.</summary>
    Local,
}
