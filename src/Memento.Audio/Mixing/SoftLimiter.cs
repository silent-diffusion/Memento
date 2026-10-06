namespace Memento.Audio.Mixing;

/// <summary>
/// Stateless soft clipper: linear up to the threshold, then a tanh knee that approaches but never reaches full scale.
/// Speech mixes rarely reach it; when two loud tracks overlap it prevents hard clipping without pumping.
/// </summary>
public static class SoftLimiter
{
    public static float Apply(float sample, float threshold)
    {
        var magnitude = MathF.Abs(sample);
        if (magnitude <= threshold)
        {
            return sample;
        }

        var headroom = 1f - threshold;
        var bent = threshold + (headroom * MathF.Tanh((magnitude - threshold) / headroom));
        return MathF.CopySign(MathF.Min(bent, 0.99999f), sample);
    }
}
