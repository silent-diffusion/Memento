namespace Memento.Core.Workers;

/// <summary>The speaker turns found in one track.</summary>
public sealed record DiarizedTrack(string TrackId, IReadOnlyList<SpeakerTurn> Turns);
