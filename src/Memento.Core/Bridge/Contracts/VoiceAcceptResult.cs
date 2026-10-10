namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>voices.acceptMatch</c>: the speakers after the rename, and the enrolment change (or <c>null</c>).</summary>
public sealed record VoiceAcceptResult(IReadOnlyList<Speaker> Speakers, string? ChangeId);
