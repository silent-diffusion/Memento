namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>agenda.importFile</c> and <c>agenda.importDropped</c>; <see cref="Preview"/> is <c>null</c> when the picker was cancelled.</summary>
public sealed record AgendaImportResult(AgendaParsePreview? Preview, bool Cancelled);
