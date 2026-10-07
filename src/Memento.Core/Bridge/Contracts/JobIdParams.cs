namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters naming one background job: <c>{ jobId }</c>.</summary>
public sealed record JobIdParams
{
    public required string JobId { get; init; }
}
