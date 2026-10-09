namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>transcript.reduceSpeakers</c>: the speakers left, each merge in the order made, and how many lines moved.</summary>
/// <param name="Basis"><c>voices</c> when the speakers' voices decided, <c>talkTime</c> when no voices were kept (least speech first).</param>
public sealed record ReduceSpeakersResult(IReadOnlyList<Speaker> Speakers, IReadOnlyList<SpeakerMerge> Merged, int SegmentsChanged, string Basis);
