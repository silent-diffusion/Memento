namespace Memento.Core.Audio;

/// <summary>What "Even out source levels" did to one track in the mix (recorded in <c>project.json</c>).</summary>
/// <param name="TrackId">The track.</param>
/// <param name="LevelDbfs">Its speech level, measured against its own noise floor; <c>null</c> when it had no speech.</param>
/// <param name="GainDb">The gain it got in the mix: 0 when left as recorded, up to <see cref="MixLevels.MaxLiftDb"/>.</param>
public sealed record MixTrackGain(string TrackId, double? LevelDbfs, double GainDb);
