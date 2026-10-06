namespace Memento.Audio.Writing;

/// <summary>Outcome of <see cref="StreamingWavWriter.Repair"/>.</summary>
/// <param name="Path">The repaired file.</param>
/// <param name="Format">Its sample format.</param>
/// <param name="DeclaredDataBytes">What the header claimed before the repair (0 if no checkpoint ever ran).</param>
/// <param name="RecoveredDataBytes">Audio bytes the header now describes (whole frames).</param>
/// <param name="TruncatedBytes">Trailing bytes of a partial frame that were cut off.</param>
/// <param name="Frames">Recovered frames.</param>
/// <param name="Duration">Recovered duration.</param>
public sealed record WavRepairResult(
    string Path,
    AudioFormat Format,
    long DeclaredDataBytes,
    long RecoveredDataBytes,
    long TruncatedBytes,
    long Frames,
    TimeSpan Duration);
