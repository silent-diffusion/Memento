namespace Memento.Core.Bridge.Contracts;

/// <summary>Payload of <c>library.moveProgress</c>.</summary>
/// <param name="State"><c>running</c>, <c>done</c> or <c>failed</c>.</param>
/// <param name="Message">What is happening or, for <c>failed</c>, what went wrong and what is safe.</param>
public sealed record LibraryMoveProgressPayload(string JobId, int Percent, string State, string? Message, string NewPath);
