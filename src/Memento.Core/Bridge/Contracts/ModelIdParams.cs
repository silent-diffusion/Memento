namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters naming one model: <c>{ modelId }</c>.</summary>
public sealed record ModelIdParams
{
    public required string ModelId { get; init; }
}
