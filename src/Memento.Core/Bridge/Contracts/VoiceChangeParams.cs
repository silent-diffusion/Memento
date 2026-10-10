namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>voices.revert</c>: the change <c>voices.remember</c> or <c>voices.acceptMatch</c> answered.</summary>
public sealed record VoiceChangeParams
{
    public required string ChangeId { get; init; }
}
