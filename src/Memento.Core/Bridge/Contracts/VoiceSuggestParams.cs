namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>voices.setSuggest</c>.</summary>
public sealed record VoiceSuggestParams
{
    public required string VoiceId { get; init; }

    public required bool Suggest { get; init; }
}
