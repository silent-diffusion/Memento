namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>voices.matches</c> and <c>voices.decline</c>: at most one suggestion per speaker and per voice.</summary>
public sealed record VoiceMatchesResult(IReadOnlyList<VoiceMatch> Matches);
