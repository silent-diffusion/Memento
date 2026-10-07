namespace Memento.Core.Bridge.Contracts;

/// <summary>
/// A cloud AI provider in the settings snapshot: whether a key is saved (the key itself never crosses the bridge) and,
/// from M4, the model requests go to and the models Settings offers.
/// </summary>
public sealed record AiProviderKeyState(bool HasKey)
{
    /// <summary>The model in effect: the override from Settings, or the default (M4).</summary>
    public string? Model { get; init; }

    /// <summary>The models Settings offers, the default first (M4). Any other id the provider serves is accepted too.</summary>
    public IReadOnlyList<string> Models { get; init; } = [];
}
