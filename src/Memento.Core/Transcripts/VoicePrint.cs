namespace Memento.Core.Transcripts;

/// <summary>
/// A voice as the voice model hears it: the unit-length direction of its embedding and how much speech it was made from.
/// Two voices are alike by the cosine of their directions; joined, they become the speech-weighted mean direction.
/// </summary>
public sealed record VoicePrint(double[] Direction, double Seconds)
{
    /// <summary>A print from a raw embedding, or <c>null</c> when it is empty or all zeros.</summary>
    public static VoicePrint? From(IReadOnlyList<float> embedding, double seconds)
    {
        ArgumentNullException.ThrowIfNull(embedding);
        if (embedding.Count == 0)
        {
            return null;
        }

        var length = Math.Sqrt(embedding.Sum(x => (double)x * x));
        return length <= 0 || double.IsNaN(length) ? null : new VoicePrint(embedding.Select(x => x / length).ToArray(), Math.Max(seconds, 0.1));
    }

    /// <summary>Cosine similarity, from −1 to 1; −∞ for prints of different models (different lengths).</summary>
    public double Similarity(VoicePrint other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (other.Direction.Length != Direction.Length)
        {
            return double.NegativeInfinity;
        }

        var sum = 0.0;
        for (var i = 0; i < Direction.Length; i++)
        {
            sum += Direction[i] * other.Direction[i];
        }

        return sum;
    }

    /// <summary>The two voices as one: the direction of their speech-weighted sum.</summary>
    public VoicePrint? Combine(VoicePrint other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (other.Direction.Length != Direction.Length)
        {
            return null;
        }

        var sum = new double[Direction.Length];
        for (var i = 0; i < sum.Length; i++)
        {
            sum[i] = (Direction[i] * Seconds) + (other.Direction[i] * other.Seconds);
        }

        var length = Math.Sqrt(sum.Sum(x => x * x));
        return length <= 0 ? null : new VoicePrint(sum.Select(x => x / length).ToArray(), Seconds + other.Seconds);
    }
}
