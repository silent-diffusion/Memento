namespace Memento.Core.Agendas;

/// <summary>An imported agenda's original file, held until <c>agenda.apply</c> copies it into the recording.</summary>
/// <param name="Path">The held copy.</param>
/// <param name="RecordingId">The recording it was imported for, or <c>null</c> when it was imported before the recording existed.</param>
/// <param name="Name">The original file name.</param>
public sealed record PendingAgendaFile(string Token, string? RecordingId, string Path, string Name, DateTimeOffset HeldAt);
