namespace Memento.Core.Bridge.Contracts;

/// <summary>"This sounds like {name}": an unnamed speaker whose voice matches a known voice (DESIGN.md §19).</summary>
/// <param name="Similarity">Cosine of the two voices, 0..1 (shown as a percentage).</param>
/// <param name="Recordings">How many recordings the known voice was confirmed in.</param>
public sealed record VoiceMatch(string SpeakerId, string VoiceId, string Name, double Similarity, int Recordings);
