namespace Memento.Audio.Mixing;

/// <summary>
/// <c>peaks.json</c>: one <c>[rms, peak]</c> pair per <see cref="WindowMs"/> window, both linear in [0, 1]
/// (3 decimals), over all channels. The UI draws the peak as the outer waveform and RMS as the inner body.
/// </summary>
public sealed record PeaksFile(int SchemaVersion, int WindowMs, IReadOnlyList<double[]> Peaks)
{
    // Never null, whatever the JSON says: a missing or null list reads as empty (security audit 2, section 9).
    public IReadOnlyList<double[]> Peaks { get; set; } = Peaks ?? [];

    public const int CurrentSchemaVersion = 1;
}
