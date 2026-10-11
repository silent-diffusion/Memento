using Memento.Core.Audio;

namespace Memento.Core.Bridge.Contracts;

/// <summary>
/// After 2.0.0-beta.1: the mix was built with "Even out source levels"; each track's speech level and the gain it got
/// (0 dB when left as recorded). <c>null</c> in <see cref="Project.MixLevels"/> when the tracks were summed as recorded.
/// </summary>
public sealed record MixLevelsInfo(DateTimeOffset At, IReadOnlyList<MixTrackGain> Tracks);
