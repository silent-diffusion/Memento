namespace Memento.Core.Bridge.Contracts;

/// <summary>One kept version of a document.</summary>
/// <param name="At">When that content was saved.</param>
/// <param name="Reason"><c>generated</c>, <c>edited</c>, <c>restored</c> or <c>regenerated</c>: how that content came to be.</param>
/// <param name="Changes">How many modules differ from the document as it is now.</param>
public sealed record DocumentVersionInfo(string Id, DateTimeOffset At, string Reason, int Changes);
