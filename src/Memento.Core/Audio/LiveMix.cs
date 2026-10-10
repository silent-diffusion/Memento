namespace Memento.Core.Audio;

/// <summary>A window of the live transcript's audio (2.0): 16 kHz mono samples in −1..1.</summary>
/// <param name="Rms">Over the whole window, after any scaling; silence is not sent to the engine.</param>
/// <param name="Tracks">How many tracks had audio in the window.</param>
public sealed record LiveMix(float[] Samples, double Rms, int Tracks)
{
    /// <summary>Below about −50 dBFS over 10 s nothing is said: the window is skipped, as the full pass skips silent windows.</summary>
    public const double SilenceRms = 0.003;

    public bool IsSilent => Rms < SilenceRms;
}
