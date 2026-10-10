namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>voices.forget</c>: <c>{ voiceId }</c>.</summary>
public sealed record VoiceIdParams
{
    public required string VoiceId { get; init; }
}
