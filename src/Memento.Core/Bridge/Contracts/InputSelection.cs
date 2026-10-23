namespace Memento.Core.Bridge.Contracts;

/// <summary>
/// "What the AI receives" (DESIGN.md §10, BRIDGE.md M4): the ticked inputs are the exact payload. Audio and video are
/// never sent and have no field.
/// </summary>
public sealed record InputSelection(bool Transcript, bool Details, bool Participants, bool Agenda, bool Highlights, bool Attachments, bool PreviousDocuments)
{
    /// <summary>A template that names no inputs: everything except attachments and earlier documents.</summary>
    public static InputSelection Default { get; } = new(true, true, true, true, true, false, false);
}
