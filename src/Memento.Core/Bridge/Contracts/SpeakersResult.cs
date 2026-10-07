namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>transcript.renameSpeaker</c>.</summary>
public sealed record SpeakersResult(IReadOnlyList<Speaker> Speakers);
