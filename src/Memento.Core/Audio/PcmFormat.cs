using System.Globalization;

namespace Memento.Core.Audio;

/// <summary>An uncompressed, interleaved sample format.</summary>
public sealed record PcmFormat(int SampleRate, int Channels, int BitsPerSample, SampleEncoding Encoding)
{
    public static PcmFormat Pcm16(int sampleRate, int channels) => new(sampleRate, channels, 16, SampleEncoding.Pcm);

    public static PcmFormat IeeeFloat32(int sampleRate, int channels) => new(sampleRate, channels, 32, SampleEncoding.IeeeFloat);

    /// <summary>Bytes per frame (one sample for every channel).</summary>
    public int BlockAlign => Channels * (BitsPerSample / 8);

    public int BytesPerSecond => SampleRate * BlockAlign;

    /// <summary>The WAV <c>wFormatTag</c>: 1 for PCM, 3 for IEEE float.</summary>
    public ushort FormatTag => Encoding == SampleEncoding.IeeeFloat ? (ushort)3 : (ushort)1;

    public long FramesFromBytes(long bytes) => bytes / BlockAlign;

    public long BytesToMilliseconds(long bytes) => FramesToMilliseconds(FramesFromBytes(bytes));

    public long FramesToMilliseconds(long frames) => frames * 1000 / SampleRate;

    public long MillisecondsToFrames(long milliseconds) => milliseconds * SampleRate / 1000;

    /// <exception cref="ArgumentException">The format is not one Memento can write and read back.</exception>
    public void Validate()
    {
        if (SampleRate is < 8_000 or > 384_000)
        {
            throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"Sample rate {SampleRate} Hz is outside 8–384 kHz."));
        }

        if (Channels is < 1 or > 8)
        {
            throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"{Channels} channels is outside 1–8."));
        }

        var supported = Encoding switch
        {
            SampleEncoding.Pcm => BitsPerSample is 8 or 16 or 24 or 32,
            SampleEncoding.IeeeFloat => BitsPerSample == 32,
            _ => false,
        };
        if (!supported)
        {
            throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"{BitsPerSample}-bit {Encoding} samples are not supported."));
        }
    }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{SampleRate} Hz, {Channels} ch, {BitsPerSample}-bit {(Encoding == SampleEncoding.IeeeFloat ? "float" : "PCM")}");
}
