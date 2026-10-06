namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters naming the recording session: <c>{ sessionId }</c>.</summary>
public sealed record SessionParams
{
    public required string SessionId { get; init; }
}
