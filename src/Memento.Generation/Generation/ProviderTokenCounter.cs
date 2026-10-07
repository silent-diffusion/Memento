using Memento.AI;

namespace Memento.Generation.Generation;

/// <summary>The provider's own token count (exact for the local model, an estimate that errs high for cloud models).</summary>
public sealed class ProviderTokenCounter(IAiProvider provider) : ITokenCounter
{
    public bool IsExact => provider.Capabilities.ExactTokenCounts;

    public int Count(string text) => provider.CountTokens(text);
}
