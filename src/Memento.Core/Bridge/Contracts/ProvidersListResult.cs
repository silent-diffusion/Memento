namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>providers.list</c>.</summary>
/// <param name="ExternalAiEnabled">Settings › AI and privacy › Allow external AI services.</param>
public sealed record ProvidersListResult(IReadOnlyList<ProviderInfo> Providers, bool ExternalAiEnabled)
{
    /// <summary>The provider a template without its own uses: the Settings default when it is set.</summary>
    public string? DefaultProviderId { get; init; }
}
