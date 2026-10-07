namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>transcript.mergeSpeakers</c>.</summary>
public sealed record MergeSpeakersResult(IReadOnlyList<Speaker> Speakers, int SegmentsChanged);
