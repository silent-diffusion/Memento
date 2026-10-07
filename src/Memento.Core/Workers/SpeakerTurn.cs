namespace Memento.Core.Workers;

/// <summary>A stretch of one voice in one track. Times are seconds on the recording timeline.</summary>
/// <param name="Speaker">0-based speaker number within the track.</param>
public sealed record SpeakerTurn(double Start, double End, int Speaker, double Confidence);
