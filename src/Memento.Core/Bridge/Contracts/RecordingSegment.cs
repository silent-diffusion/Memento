namespace Memento.Core.Bridge.Contracts;

/// <summary>
/// 2.1: one recorded stretch of a recording in <c>project.get</c> (<see cref="Project.Segments"/>). Segments sit end to
/// end on the timeline; <paramref name="GapMs"/> is the real time between the previous one's stop and this one's start,
/// shown as a pause ("Resumed at 10:42 · paused 12 min").
/// </summary>
/// <param name="Index">1 for the original recording, 2 for the first resume, and so on.</param>
/// <param name="StartedAt">When the segment started.</param>
/// <param name="OffsetMs">Where it starts on the recording timeline.</param>
/// <param name="DurationMs">Its recorded time, pauses excluded.</param>
/// <param name="GapMs">Real time since the previous segment stopped; 0 for the first.</param>
public sealed record RecordingSegment(int Index, DateTimeOffset StartedAt, long OffsetMs, long DurationMs, long GapMs);
