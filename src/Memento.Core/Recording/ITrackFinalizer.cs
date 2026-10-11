namespace Memento.Core.Recording;

/// <summary>
/// Turns a stopped session's capture WAVs into the stored recording: each track encoded to the storage format,
/// <c>mix.&lt;ext&gt;</c>, <c>peaks.json</c>, and a SHA-256 for every track and the mix. Never modifies a capture
/// file it has not finished replacing; safe to run again after an interruption.
/// </summary>
public interface ITrackFinalizer
{
    Task<FinalizedAudio> FinalizeAsync(FinalizeRequest request, IProgress<int>? progress, CancellationToken cancellationToken);

    /// <summary>
    /// Mixes a finished recording's stored tracks again (after 2.0.0-beta.1, "Rebuild the mix"), with or without evened
    /// levels, into new files named by <see cref="MixRebuildRequest.Stem"/> beside the current mix, which it never
    /// touches; the tracks are only read. Leaves nothing behind when it fails or is cancelled.
    /// </summary>
    Task<RebuiltMix> RebuildMixAsync(MixRebuildRequest request, IProgress<int>? progress, CancellationToken cancellationToken);
}
