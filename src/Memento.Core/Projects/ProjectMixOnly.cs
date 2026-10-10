namespace Memento.Core.Projects;

/// <summary>
/// Set in <c>project.json</c> once "Keep only the mix" removed the recording's separate track files (2.0). The tracks
/// stay listed (their names, offsets and lengths, which the transcript's lines and the speakers refer to) without a
/// hash, and only the mix is kept: speakers cannot be identified per track again and tracks cannot be exported.
/// </summary>
/// <param name="At">When the track files were removed.</param>
/// <param name="TrackIds">The tracks whose files were removed.</param>
/// <param name="BytesFreed">The size of the removed files.</param>
public sealed record ProjectMixOnly(DateTimeOffset At, IReadOnlyList<string> TrackIds, long BytesFreed);
