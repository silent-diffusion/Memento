namespace Memento.Transcription.Words;

/// <summary>A Whisper segment before words are built: times relative to the audio window.</summary>
/// <param name="MinProbability">The engine's lowest token probability, used when there are no tokens.</param>
public sealed record RawSegment(double Start, double End, string Text, double MinProbability, IReadOnlyList<TokenInfo> Tokens);
