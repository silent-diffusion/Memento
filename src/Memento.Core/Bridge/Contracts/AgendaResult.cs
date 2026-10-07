namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>agenda.setCovered</c>: the whole agenda after the change.</summary>
public sealed record AgendaResult(Agenda Agenda);
