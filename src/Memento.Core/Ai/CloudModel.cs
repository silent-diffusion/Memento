namespace Memento.Core.Ai;

/// <summary>
/// A cloud model as Memento sends to it: the id, the plain-language note Settings shows, how much one request may hold,
/// and which request settings it accepts. A model id typed by hand ("Other model…") gets a conservative profile
/// (<see cref="AiModelDefaults.Describe"/>): <see cref="Listed"/> is false and nothing model-specific is sent.
/// </summary>
/// <param name="Id">The provider's model id, exactly as sent ("claude-opus-5-5").</param>
/// <param name="Note">One short sentence for Settings ("The fastest and cheapest.").</param>
/// <param name="ContextTokens">Prompt plus answer tokens one request may use.</param>
/// <param name="MaxOutputTokens">The most answer tokens one request may ask for.</param>
public sealed record CloudModel(string Id, string Note, int ContextTokens, int MaxOutputTokens)
{
    /// <summary>In Memento's list. False for an id typed under "Other model…".</summary>
    public bool Listed { get; init; } = true;

    /// <summary>Send the effort setting (Claude <c>output_config.effort</c>, OpenAI <c>reasoning.effort</c>).</summary>
    public bool SendsEffort { get; init; } = true;

    /// <summary>Claude only: send <c>fallbacks: "default"</c> (server-side refusal fallback; Claude Haiku 5.5 has none).</summary>
    public bool ServerSideFallback { get; init; }
}
