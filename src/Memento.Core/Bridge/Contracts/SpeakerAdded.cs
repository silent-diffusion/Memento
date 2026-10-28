namespace Memento.Core.Bridge.Contracts;

/// <summary>
/// When a name was added under Who spoke while recording (2.7.0). The speakers stage reads it as a hint: a name added
/// partway through is most likely someone heard from then on, so it breaks ties towards a voice that talks after it and
/// flags a Speaking now mark that heard a different voice than the one the name went to.
/// </summary>
/// <param name="Name">One of <see cref="WhoSpoke.Names"/>, as spelled there.</param>
/// <param name="AtMs">The recording timer's time when the name was added (recorded time, excluding pauses), 0 or more.</param>
/// <param name="At">The wall-clock time it was added.</param>
public sealed record SpeakerAdded(string Name, long AtMs, DateTimeOffset At);
