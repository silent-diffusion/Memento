namespace Memento.Core.Bridge.Contracts;

/// <summary>2.0: one input in "Preview what will be sent": whether the template ticks it, whether it is in the payload, and its size.</summary>
/// <param name="Key">An <see cref="InputSelection"/> field (<c>transcript</c>, <c>details</c>, <c>participants</c>, <c>agenda</c>, <c>highlights</c>, <c>attachments</c>, <c>previousDocuments</c>) or <c>instructions</c>.</param>
/// <param name="Label">"Transcript", "Highlights and notes".</param>
/// <param name="Ticked">The template's checklist ticks it (instructions: always).</param>
/// <param name="Included">Its text is in the payload.</param>
/// <param name="Characters">Characters of its text in the payload (0 when not included).</param>
/// <param name="Bytes">UTF-8 bytes of its text in the payload.</param>
/// <param name="Summary">"318 segments, 4 speakers", or <c>null</c>.</param>
/// <param name="Note">Why a ticked input is not sent, or what was left out of it; <c>null</c> otherwise.</param>
public sealed record PreviewInput(string Key, string Label, bool Ticked, bool Included, long Characters, long Bytes, string? Summary, string? Note);
