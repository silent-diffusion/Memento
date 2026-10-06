using System.Globalization;

namespace Memento.Audio;

/// <summary>
/// An interleaved PCM or float sample format. <see cref="BitsPerSample"/> is the container size;
/// <see cref="ValidBitsPerSample"/> is the significant bits (24 in a 32-bit container, for example).
/// </summary>
public sealed record AudioFormat
{
    public AudioFormat(int sampleRate, int channels, int bitsPerSample, AudioSampleEncoding encoding, int validBitsPerSample = 0)
    {
        if (sampleRate is < 1000 or > 768_000)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be between 1 kHz and 768 kHz.");
        }

        if (channels is < 1 or > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(channels), channels, "Channel count must be between 1 and 32.");
        }

        var supported = encoding == AudioSampleEncoding.IeeeFloat
            ? bitsPerSample is 32 or 64
            : bitsPerSample is 8 or 16 or 24 or 32;
        if (!supported)
        {
            throw new ArgumentOutOfRangeException(nameof(bitsPerSample), bitsPerSample, $"{bitsPerSample}-bit {encoding} samples are not supported.");
        }

        if (validBitsPerSample < 0 || validBitsPerSample > bitsPerSample)
        {
            throw new ArgumentOutOfRangeException(nameof(validBitsPerSample), validBitsPerSample, "Valid bits cannot exceed the container size.");
        }

        SampleRate = sampleRate;
        Channels = channels;
        BitsPerSample = bitsPerSample;
        Encoding = encoding;
        ValidBitsPerSample = validBitsPerSample == 0 ? bitsPerSample : validBitsPerSample;
    }

    public int SampleRate { get; }

    public int Channels { get; }

    /// <summary>Container size of one sample in bits.</summary>
    public int BitsPerSample { get; }

    public AudioSampleEncoding Encoding { get; }

    public int ValidBitsPerSample { get; }

    public int BytesPerSample => BitsPerSample / 8;

    /// <summary>Bytes per interleaved frame (one sample for every channel).</summary>
    public int BlockAlign => Channels * BytesPerSample;

    public int BytesPerSecond => SampleRate * BlockAlign;

    public bool IsFloat => Encoding == AudioSampleEncoding.IeeeFloat;

    /// <summary>The WASAPI shared-mode default on current Windows, and the process-loopback request format.</summary>
    public static AudioFormat Float32Stereo48k { get; } = IeeeFloat32(48_000, 2);

    public static AudioFormat IeeeFloat32(int sampleRate, int channels) => new(sampleRate, channels, 32, AudioSampleEncoding.IeeeFloat);

    public static AudioFormat Pcm16(int sampleRate, int channels) => new(sampleRate, channels, 16, AudioSampleEncoding.Pcm);

    public static AudioFormat Pcm24(int sampleRate, int channels) => new(sampleRate, channels, 24, AudioSampleEncoding.Pcm);

    public TimeSpan DurationOf(long frames) => TimeSpan.FromTicks(QpcClock.FramesToTicks(frames, SampleRate));

    public override string ToString()
    {
        var kind = IsFloat ? "float" : "int";
        var bits = ValidBitsPerSample == BitsPerSample
            ? BitsPerSample.ToString(CultureInfo.InvariantCulture)
            : string.Create(CultureInfo.InvariantCulture, $"{ValidBitsPerSample}in{BitsPerSample}");
        return string.Create(CultureInfo.InvariantCulture, $"{SampleRate} Hz, {Channels} ch, {kind}{bits}");
    }
}
