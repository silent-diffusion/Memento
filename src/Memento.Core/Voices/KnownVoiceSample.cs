namespace Memento.Core.Voices;

/// <summary>
/// One confirmation of a known voice: the voice of a speaker the user named in one recording, as the voice model heard
/// it (a unit-length direction, never audio).
/// </summary>
/// <param name="RecordingId">The recording the speaker was named in.</param>
/// <param name="SpeakerId">The speaker in that recording's transcript.</param>
/// <param name="Embedding">The unit-length direction of the speaker's voice.</param>
/// <param name="Seconds">How much speech the voice was mixed from.</param>
/// <param name="At">When the name was confirmed.</param>
public sealed record KnownVoiceSample(string RecordingId, string SpeakerId, IReadOnlyList<float> Embedding, double Seconds, DateTimeOffset At);
