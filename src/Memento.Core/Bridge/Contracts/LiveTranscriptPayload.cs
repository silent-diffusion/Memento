namespace Memento.Core.Bridge.Contracts;

/// <summary>
/// Payload of <c>recording.liveTranscript</c> (2.0): the whole provisional draft of a session so far, sent after every
/// 10-second window and whenever <see cref="State"/> changes. Each payload replaces the last; the full pass after Stop
/// replaces the draft entirely, and the draft is never saved.
/// </summary>
/// <param name="State">
/// <c>starting</c> (loading the model), <c>listening</c>, <c>paused</c> (the recording is paused, or live yields to a
/// full transcription pass or a busy processor; <see cref="Note"/> says which), <c>unavailable</c> (no Small or Base
/// model installed) or <c>failed</c> (the live worker stopped; the recording is not affected).
/// </param>
/// <param name="Engine">Where it runs, for the card's pill: "Local · CPU · Small", "Local · GPU · Small"; <c>null</c> before the model is loaded.</param>
/// <param name="Note">A §17 sentence for <c>paused</c>, <c>unavailable</c> and <c>failed</c>; otherwise <c>null</c>.</param>
public sealed record LiveTranscriptPayload(
    string SessionId,
    IReadOnlyList<LiveTranscriptSegment> Segments,
    string State = LiveTranscriptStates.Listening,
    string? Engine = null,
    string? Note = null);
