namespace Memento.Core.Bridge.Contracts;

/// <summary>
/// A cloud AI provider in the settings snapshot: whether a key is saved (the key itself never crosses the bridge) and,
/// from M4, the model requests go to and the models Settings offers.
/// </summary>
public sealed record AiProviderKeyState(bool HasKey)
{
    /// <summary>The model in effect: the one chosen in Settings, or the default (M4).</summary>
    public string? Model { get; init; }

    /// <summary>The models Settings offers, the default first (M4). Any other id the provider serves is accepted too.</summary>
    public IReadOnlyList<string> Models { get; init; } = [];

    /// <summary>The same models with their notes and context sizes, in the same order (after 2.1).</summary>
    public IReadOnlyList<AiModelChoice> Choices { get; init; } = [];

    /// <summary><c>true</c> when <see cref="Model"/> was typed under "Other model…" and is not in <see cref="Models"/> (after 2.1).</summary>
    public bool Custom { get; init; }

    /// <summary>The note for <see cref="Model"/>: its list note, or what Memento assumes about a model it does not know (after 2.1).</summary>
    public string? ModelNote { get; init; }
}
