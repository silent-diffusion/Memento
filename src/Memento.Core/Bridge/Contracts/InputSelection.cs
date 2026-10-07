namespace Memento.Core.Bridge.Contracts;

/// <summary>
/// "What the AI receives" (DESIGN.md §10, BRIDGE.md M4): the ticked inputs are the exact payload. Audio and video are
/// never sent and have no field.
/// </summary>
public sealed record InputSelection(bool Transcript, bool Details, bool Participants, bool Agenda, bool Highlights, bool Attachments, bool PreviousDocuments);
