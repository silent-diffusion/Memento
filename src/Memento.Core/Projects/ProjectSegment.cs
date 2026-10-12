namespace Memento.Core.Projects;

/// <summary>
/// One recorded stretch of a recording (schema v5, 2.1): the first is the original recording, each later one was added by
/// "Resume recording" after a Stop. Segments sit end to end on the recording timeline; the real time between them is
/// <see cref="GapMs"/> and is shown as a pause, never recorded as silence.
/// </summary>
/// <param name="Index">1 for the original recording, 2 for the first resume, and so on.</param>
/// <param name="StartedAt">When the segment started, local time with its UTC offset.</param>
/// <param name="OffsetMs">Where the segment starts on the recording timeline (the recorded time before it).</param>
/// <param name="DurationMs">Recorded time of the segment, pauses excluded; 0 while it is being recorded.</param>
/// <param name="GapMs">Real time between the end of the previous segment and this one; 0 for the first.</param>
/// <param name="EndedAt">When the segment stopped, or <c>null</c> while it is being recorded (or unknown, for a migrated recording).</param>
public sealed record ProjectSegment(int Index, DateTimeOffset StartedAt, long OffsetMs, long DurationMs, long GapMs, DateTimeOffset? EndedAt);
