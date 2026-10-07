namespace Memento.Core.Bridge.Contracts;

/// <summary>One rough segment of the live transcript (seconds since the recording started).</summary>
public sealed record LiveTranscriptSegment(double Start, double End, string Text);
