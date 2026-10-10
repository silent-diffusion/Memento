using System.Buffers.Binary;
using Memento.Core.Audio;

namespace Memento.Core.Recording.Simulation;

/// <summary>
/// A distinct, deterministic test signal per source kind, written as 16-bit PCM:
/// microphone — a speech-like voiced tone in phrases with syllable rhythm;
/// system — a two-tone chord slowly panned between channels;
/// application — a triangle tone with tremolo.
/// </summary>
internal sealed class SimulatedSignal
{
    private readonly string _kind;
    private readonly PcmFormat _format;
    private readonly double _baseFrequency;
    private uint _noise;
    private long _frame;

    private readonly float[]? _speech;
    private readonly int _speechRate;

    public SimulatedSignal(string kind, PcmFormat format, int variant, int seed, float[]? speech = null, int speechRate = 16_000)
    {
        _kind = kind;
        _format = format;
        _baseFrequency = kind switch
        {
            "microphone" => 140 + (variant * 35),
            "system" => 440,
            _ => 330 + (variant * 20),
        };
        _noise = (uint)(seed * 2654435761u) | 1u;
        _speech = kind == "microphone" && speech is { Length: > 0 } ? speech : null;
        _speechRate = speechRate;
    }

    /// <summary>Writes <paramref name="frames"/> frames into <paramref name="pcm"/>; returns RMS and peak (0..1).</summary>
    public (float Rms, float Peak) Fill(Span<byte> pcm, int frames)
    {
        var rate = (double)_format.SampleRate;
        var channels = _format.Channels;
        double sumSquares = 0;
        float peak = 0;
        for (var f = 0; f < frames; f++, _frame++)
        {
            var t = _frame / rate;
            for (var c = 0; c < channels; c++)
            {
                var value = (float)Sample(t, c);
                var magnitude = MathF.Abs(value);
                peak = MathF.Max(peak, magnitude);
                sumSquares += value * value;
                var s = (short)Math.Clamp(MathF.Round(value * short.MaxValue), short.MinValue, short.MaxValue);
                BinaryPrimitives.WriteInt16LittleEndian(pcm[(((f * channels) + c) * 2)..], s);
            }
        }

        var count = Math.Max(1, frames * channels);
        return ((float)Math.Sqrt(sumSquares / count), peak);
    }

    private double Sample(double t, int channel)
    {
        var noise = NextNoise() * 0.01;
        switch (_kind)
        {
            case "microphone" when _speech is { } clip:
            {
                // A speech clip in a loop (linear interpolation from its own rate).
                var position = (t * _speechRate) % clip.Length;
                var index = (int)position;
                var next = clip[(index + 1) % clip.Length];
                return clip[index] + ((next - clip[index]) * (position - index)) + (noise * 0.1);
            }

            case "microphone":
            {
                // Phrases of 2.5 s with 0.8 s gaps; syllables at about 4 per second; a voiced tone with harmonics.
                var phrase = (t % 3.3) < 2.5 ? 1.0 : 0.0;
                var syllable = 0.5 * (1 + Math.Sin(2 * Math.PI * 4.1 * t));
                var f0 = _baseFrequency * (1 + (0.03 * Math.Sin(2 * Math.PI * 0.7 * t)));
                var voice = Math.Sin(2 * Math.PI * f0 * t)
                    + (0.5 * Math.Sin(2 * Math.PI * 2 * f0 * t))
                    + (0.25 * Math.Sin(2 * Math.PI * 3 * f0 * t));
                return (0.2 * phrase * syllable * voice) + noise;
            }

            case "system":
            {
                var pan = 0.5 * (1 + Math.Sin(2 * Math.PI * 0.1 * t));
                var gain = channel == 0 ? pan : 1 - pan;
                var chord = Math.Sin(2 * Math.PI * _baseFrequency * t) + Math.Sin(2 * Math.PI * _baseFrequency * 1.26 * t);
                return (0.15 * gain * chord) + (noise * 2);
            }

            default:
            {
                var phase = (t * _baseFrequency) % 1.0;
                var triangle = (4 * Math.Abs(phase - 0.5)) - 1;
                var tremolo = 0.6 + (0.4 * Math.Sin(2 * Math.PI * 6 * t));
                return (0.2 * tremolo * triangle) + noise;
            }
        }
    }

    private double NextNoise()
    {
        // xorshift32: fast, deterministic, good enough for a noise floor.
        _noise ^= _noise << 13;
        _noise ^= _noise >> 17;
        _noise ^= _noise << 5;
        return (_noise / (double)uint.MaxValue * 2) - 1;
    }
}
