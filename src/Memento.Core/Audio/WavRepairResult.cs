namespace Memento.Core.Audio;

/// <summary>Outcome of <see cref="WavRepair.Repair"/>.</summary>
/// <param name="Succeeded">The file is now a valid WAV (possibly with no samples).</param>
/// <param name="DataBytes">Sample bytes the repaired header describes.</param>
/// <param name="Changed">The header or length had to be rewritten.</param>
/// <param name="TruncatedBytes">Bytes of a trailing partial frame that were cut.</param>
/// <param name="Problem">Why the file could not be repaired, when <paramref name="Succeeded"/> is false.</param>
public sealed record WavRepairResult(
    string Path,
    bool Succeeded,
    PcmFormat? Format,
    long DataBytes,
    long DurationMs,
    bool Changed,
    long TruncatedBytes,
    string? Problem)
{
    public static WavRepairResult Failed(string path, string problem) => new(path, false, null, 0, 0, false, 0, problem);
}
