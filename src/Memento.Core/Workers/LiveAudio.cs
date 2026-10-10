namespace Memento.Core.Workers;

/// <summary>
/// One window of the live transcript (2.0), sent on the worker's input as <c>{"type":"audio","audio":{…}}</c>.
/// </summary>
/// <param name="Window">The window's number in the session (0 = the first 10 seconds).</param>
/// <param name="StartSeconds">Where the window starts on the recording timeline; the heard lines are shifted by it.</param>
/// <param name="Pcm16">16 kHz mono 16-bit little-endian samples, base64 (320 KB of samples for 10 s, about 427 KB of text).</param>
public sealed record LiveAudio(int Window, double StartSeconds, string Pcm16)
{
    public const int SampleRate = 16_000;

    /// <summary>Encodes samples in −1..1 (clipped).</summary>
    public static string Encode(ReadOnlySpan<float> samples)
    {
        var bytes = new byte[samples.Length * 2];
        for (var i = 0; i < samples.Length; i++)
        {
            var value = (short)Math.Round(Math.Clamp(samples[i], -1f, 1f) * short.MaxValue);
            bytes[2 * i] = (byte)value;
            bytes[(2 * i) + 1] = (byte)(value >> 8);
        }

        return Convert.ToBase64String(bytes);
    }

    /// <summary>The samples of <see cref="Pcm16"/> in −1..1.</summary>
    /// <exception cref="FormatException">Not base64.</exception>
    public float[] Decode()
    {
        var bytes = Convert.FromBase64String(Pcm16);
        var samples = new float[bytes.Length / 2];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = (short)(bytes[2 * i] | (bytes[(2 * i) + 1] << 8)) / (float)short.MaxValue;
        }

        return samples;
    }
}
