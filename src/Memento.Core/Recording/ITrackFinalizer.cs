namespace Memento.Core.Recording;

/// <summary>
/// Turns a stopped session's capture WAVs into the stored recording: each track encoded to the storage format,
/// <c>mix.&lt;ext&gt;</c>, <c>peaks.json</c>, and a SHA-256 for every track and the mix. Never modifies a capture
/// file it has not finished replacing; safe to run again after an interruption.
/// </summary>
public interface ITrackFinalizer
{
    Task<FinalizedAudio> FinalizeAsync(FinalizeRequest request, IProgress<int>? progress, CancellationToken cancellationToken);
}
