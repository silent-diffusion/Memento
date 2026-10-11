namespace Memento.Core.Recording;

/// <summary>What <see cref="ITrackFinalizer.RebuildMixAsync"/> wrote beside the current mix (relative paths).</summary>
/// <param name="Mix">The new mix (<c>mix.rebuilt.flac</c>), complete, verified and hashed.</param>
/// <param name="PeaksFile">Its waveform peaks (<c>mix.rebuilt.peaks.json</c>).</param>
/// <param name="Gains">The gain each track got; <c>null</c> when it was mixed as recorded.</param>
public sealed record RebuiltMix(FinalizedFile Mix, string PeaksFile, IReadOnlyList<Audio.MixTrackGain>? Gains);
