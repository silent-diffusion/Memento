namespace Memento.Core.Recording;

/// <summary>
/// Input to <see cref="ITrackFinalizer.RebuildMixAsync"/>: the stored tracks of a finished recording, mixed again with or
/// without "Even out source levels" into new files beside the current mix (the <c>mix</c> stage then swaps them in).
/// </summary>
/// <param name="ProjectFolder">Full path of the project folder.</param>
/// <param name="Tracks">The stored track files (FLAC, WAV or a smaller format), read only.</param>
/// <param name="EvenLevels">Lift a quiet track to the loudest one's speech level.</param>
/// <param name="Stem">The new mix's name without extension (<c>mix.rebuilt</c>); <c>&lt;stem&gt;.peaks.json</c> beside it.</param>
public sealed record MixRebuildRequest(string ProjectFolder, IReadOnlyList<MixRebuildTrack> Tracks, bool EvenLevels, string Stem);
