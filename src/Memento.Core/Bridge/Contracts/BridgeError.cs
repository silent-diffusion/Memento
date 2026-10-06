namespace Memento.Core.Bridge.Contracts;

/// <summary>
/// A structured failure. <see cref="Message"/> is written for people (DESIGN.md §17);
/// <see cref="Detail"/> is optional diagnostic context and never contains a stack trace.
/// </summary>
public sealed record BridgeError(string Code, string Message, string? Detail);
