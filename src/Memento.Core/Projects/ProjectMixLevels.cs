using Memento.Core.Audio;

namespace Memento.Core.Projects;

/// <summary>The mix was built with "Even out source levels" (ENGINE-NOTES.md §Q).</summary>
/// <param name="At">When the mix was built.</param>
/// <param name="Tracks">Each track's speech level and the gain it got (0 dB when left as recorded).</param>
public sealed record ProjectMixLevels(DateTimeOffset At, IReadOnlyList<MixTrackGain> Tracks);
