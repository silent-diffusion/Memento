namespace Memento.Video.Recovery;

/// <summary>
/// Makes a video track that was being written when Memento stopped unexpectedly playable (ARCHITECTURE.md §5.x,
/// ENGINE-NOTES.md §M.3). The capture file is fragmented MP4, so everything up to the last complete fragment is
/// intact; only a torn tail (an incomplete fragment, never playable) is cut off. The result is remuxed into an
/// ordinary MP4 the way a normal stop finalizes it. The capture file is not deleted here; the caller removes it once
/// the output is in the manifest.
/// </summary>
public static class VideoRecovery
{
    /// <summary>Cuts the torn tail of <paramref name="capturePath"/> (if any) and remuxes it into <paramref name="outputPath"/>.</summary>
    public static async Task<VideoRecoveryResult> RecoverAsync(string capturePath, string outputPath, CancellationToken cancellationToken)
    {
        var repair = Repair(capturePath);
        if (repair.Fragments == 0)
        {
            return repair;
        }

        var remux = await Mp4Remuxer.RemuxAsync([new Mp4Remuxer.Part(capturePath, 0)], outputPath, cancellationToken).ConfigureAwait(false);
        return repair with { OutputBytes = remux.OutputBytes, Samples = remux.Samples };
    }

    /// <summary>
    /// Cuts an incomplete trailing fragment off a fragmented capture file in place so every reader accepts it. A file
    /// that is already whole, or is not fragmented, is left untouched.
    /// </summary>
    public static VideoRecoveryResult Repair(string capturePath)
    {
        Mp4FileSummary summary;
        using (var stream = new FileStream(capturePath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
        {
            summary = Mp4FileSummary.Read(stream);
            if (summary.IsFragmented && summary.TornBytes > 0 && summary.ValidLength > 0)
            {
                stream.SetLength(summary.ValidLength);
                stream.Flush(flushToDisk: true);
            }
        }

        return new VideoRecoveryResult(
            summary.HasMoov && summary.IsFragmented,
            summary.Fragments.Count,
            summary.DurationSeconds,
            summary.IsFragmented ? summary.TornBytes : 0,
            summary.Samples,
            0);
    }
}
