namespace Memento.Core.Bridge.Contracts;

/// <summary>
/// A "Speaking now" mark (2.6.0): the user pressed the button beside a name under Who spoke while that person was
/// talking. The speakers stage names the voice heard at that moment after them. Times are milliseconds on the recording
/// timeline (recorded time, excluding pauses), as the recording timer shows them.
/// </summary>
/// <param name="Name">One of <see cref="WhoSpoke.Names"/>, as spelled there.</param>
/// <param name="AtMs">When the button was pressed, 0 or more.</param>
public sealed record SpokeMark(string Name, long AtMs);
