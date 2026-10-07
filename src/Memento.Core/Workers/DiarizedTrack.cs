namespace Memento.Core.Workers;

/// <summary>The speaker turns found in one track, and each speaker's voice embedding.</summary>
/// <param name="Voices">One per speaker number that has enough speech; <c>null</c> from a worker that did not make them.</param>
/// <param name="AudioSeconds">Length of the track's audio.</param>
public sealed record DiarizedTrack(string TrackId, IReadOnlyList<SpeakerTurn> Turns, IReadOnlyList<SpeakerVoice>? Voices = null, double AudioSeconds = 0);
