namespace Memento.Core.Bridge.Contracts;

/// <summary>Settings › AI and privacy › What may be shared. Audio and video are never shared and have no switch.</summary>
public sealed record AiShareSnapshot(bool Transcript, bool Details, bool Participants, bool Agenda, bool Highlights, bool Attachments);
