namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>voices.remember</c>.</summary>
/// <param name="Remembered">The voice was learned or refined.</param>
/// <param name="ChangeId">Undoes it with <c>voices.revert</c>; <c>null</c> when nothing changed.</param>
/// <param name="Voice">The known voice after the change, or <c>null</c>.</param>
/// <param name="Reason">Why nothing was remembered (setting off, too little speech, no voices kept), or <c>null</c>.</param>
public sealed record VoiceRememberResult(bool Remembered, string? ChangeId, KnownVoiceInfo? Voice, string? Reason);
