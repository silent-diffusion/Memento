namespace Memento.Core.Bridge.Contracts;

/// <summary>
/// Result of <c>transcript.sameVoice</c>: the lines that share a voice with the asked ones (without them), in transcript
/// order; empty when no voices were kept or none holds an asked line.
/// </summary>
public sealed record TranscriptSameVoiceResult(IReadOnlyList<string> SegmentIds);
