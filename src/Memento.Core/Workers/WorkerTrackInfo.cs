namespace Memento.Core.Workers;

/// <summary>What the worker found in a track before transcribing it.</summary>
/// <param name="Silent">No speech energy anywhere: the track is skipped.</param>
/// <param name="Speech">Speech-energy regions, <c>[start, end]</c> seconds on the recording timeline, for the coverage check.</param>
/// <param name="Windows">How many windows the track is transcribed in.</param>
public sealed record WorkerTrackInfo(string TrackId, double DurationSeconds, bool Silent, double Rms, IReadOnlyList<double[]> Speech, int Windows);
