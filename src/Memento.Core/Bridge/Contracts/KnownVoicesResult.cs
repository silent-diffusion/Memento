namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>voices.list</c>, <c>voices.setSuggest</c>, <c>voices.forget</c> and <c>voices.forgetAll</c>.</summary>
/// <param name="Remember">Settings › Speakers › "Remember speakers by voice".</param>
/// <param name="Voices">Every known voice, by name.</param>
public sealed record KnownVoicesResult(bool Remember, IReadOnlyList<KnownVoiceInfo> Voices);
