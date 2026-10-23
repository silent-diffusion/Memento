namespace Memento.Transcription.Speakers;

/// <summary>
/// A line the user placed on a speaker (<c>speakerByUser</c>), as the next pass sees it (2.6.0): the line keeps the person
/// called <see cref="Label"/> (the speaker's name, which is what survives a pass), and the raw voice that won the line is
/// pinned to that person, so it is joined with the other voices pinned to the same label first and never with a voice
/// pinned to another. <see cref="Named"/> says the label is a name the user gave (<c>renamed</c>): a default "Speaker 3"
/// pins just the same, but only a given name is carried to the new speaker.
/// </summary>
public sealed record SpeakerPin(string SegmentId, string Label, bool Named);
