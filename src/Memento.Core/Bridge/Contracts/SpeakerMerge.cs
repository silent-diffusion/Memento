namespace Memento.Core.Bridge.Contracts;

/// <summary>
/// One speaker folded into another by <c>transcript.reduceSpeakers</c>: the speaker as it was and the lines it had at that
/// moment (which include lines of speakers folded into it earlier), so Undo can put it back with
/// <c>transcript.restoreSpeakers</c>, last merge first.
/// </summary>
public sealed record SpeakerMerge(SpeakerRestore Speaker, string IntoSpeakerId, IReadOnlyList<string> SegmentIds);
