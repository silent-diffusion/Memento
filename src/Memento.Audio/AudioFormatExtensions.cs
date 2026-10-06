using NAudio.Wave;

namespace Memento.Audio;

/// <summary>Conversions between <see cref="AudioFormat"/> and NAudio's <see cref="WaveFormat"/>.</summary>
internal static class AudioFormatExtensions
{
    public static WaveFormat ToWaveFormat(this AudioFormat format)
    {
        if (format.IsFloat)
        {
            return WaveFormat.CreateIeeeFloatWaveFormat(format.SampleRate, format.Channels);
        }

        return format.ValidBitsPerSample == format.BitsPerSample && format.Channels <= 2
            ? new WaveFormat(format.SampleRate, format.BitsPerSample, format.Channels)
            : new WaveFormatExtensible(format.SampleRate, format.BitsPerSample, format.Channels);
    }

    /// <summary>Container format of <paramref name="format"/> (valid-bits detail is not carried over).</summary>
    public static AudioFormat ToAudioFormat(this WaveFormat format)
    {
        var isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat
            || (format is WaveFormatExtensible ext && ext.SubFormat == NAudio.Dmo.AudioMediaSubtypes.MEDIASUBTYPE_IEEE_FLOAT);
        return new AudioFormat(format.SampleRate, format.Channels, format.BitsPerSample, isFloat ? AudioSampleEncoding.IeeeFloat : AudioSampleEncoding.Pcm);
    }
}
