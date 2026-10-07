namespace Memento.Core.Bridge.Contracts;

/// <summary>Something the user should know about an agenda import that is not tied to one item.</summary>
public sealed record AgendaWarning(string Code, string Message);
