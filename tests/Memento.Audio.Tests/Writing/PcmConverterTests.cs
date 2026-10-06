using Memento.Audio.Writing;

namespace Memento.Audio.Tests.Writing;

public sealed class PcmConverterTests
{
    [Theory]
    [InlineData(0f, 0x00, 0x00, 0x00)]
    [InlineData(1f, 0xFF, 0xFF, 0x7F)]
    [InlineData(-1f, 0x01, 0x00, 0x80)]
    [InlineData(0.5f, 0x00, 0x00, 0x40)]      // 4 194 303.5 rounds to even: 4 194 304
    [InlineData(-0.25f, 0x00, 0x00, 0xE0)]    // -2 097 151.75 → -2 097 152
    [InlineData(2f, 0xFF, 0xFF, 0x7F)]        // clamped
    [InlineData(-3f, 0x01, 0x00, 0x80)]       // clamped
    [InlineData(float.NaN, 0x00, 0x00, 0x00)]
    public void FloatToInt24ProducesKnownBytes(float value, int b0, int b1, int b2)
    {
        var bytes = new byte[3];
        PcmConverter.FloatToInt24([value], bytes);

        Assert.Equal(new[] { (byte)b0, (byte)b1, (byte)b2 }, bytes);
    }

    [Fact]
    public void SmallestStepRoundTripsThroughTheIntegerValue()
    {
        Assert.Equal(1, PcmConverter.FloatToInt24(1f / PcmConverter.Int24Max));
        Assert.Equal(-1, PcmConverter.FloatToInt24(-1f / PcmConverter.Int24Max));
        Assert.Equal(0, PcmConverter.FloatToInt24(0.4f / PcmConverter.Int24Max));
    }

    [Fact]
    public void Int24ReadBackIsSignExtended()
    {
        Assert.Equal(-8_388_607, PcmConverter.ReadInt24([0x01, 0x00, 0x80]));
        Assert.Equal(8_388_607, PcmConverter.ReadInt24([0xFF, 0xFF, 0x7F]));
        Assert.Equal(-1, PcmConverter.ReadInt24([0xFF, 0xFF, 0xFF]));
    }

    [Fact]
    public void StorageFormatIsInt24ForFloatAndWideIntegersAndInt16OtherwisePassesThrough()
    {
        Assert.Equal(AudioFormat.Pcm24(48_000, 2), PcmConverter.StorageFormatFor(AudioFormat.Float32Stereo48k));
        Assert.Equal(AudioFormat.Pcm24(44_100, 1), PcmConverter.StorageFormatFor(AudioFormat.Pcm24(44_100, 1)));
        Assert.Equal(AudioFormat.Pcm24(48_000, 2), PcmConverter.StorageFormatFor(new AudioFormat(48_000, 2, 32, AudioSampleEncoding.Pcm, 24)));
        Assert.Equal(AudioFormat.Pcm16(16_000, 1), PcmConverter.StorageFormatFor(AudioFormat.Pcm16(16_000, 1)));
    }

    [Fact]
    public void ConvertsFloatFramesAndPassesInt16Through()
    {
        var floats = new[] { 0.5f, -0.5f, 1f, -1f };
        var output = new byte[12];
        var n = PcmConverter.ToStorage(Signals.AsBytes(floats), AudioFormat.Float32Stereo48k, output);
        Assert.Equal(12, n);
        Assert.Equal(new byte[] { 0x00, 0x00, 0x40, 0x00, 0x00, 0xC0, 0xFF, 0xFF, 0x7F, 0x01, 0x00, 0x80 }, output);

        var int16 = new byte[] { 1, 2, 3, 4 };
        var copy = new byte[4];
        PcmConverter.ToStorage(int16, AudioFormat.Pcm16(48_000, 2), copy);
        Assert.Equal(int16, copy);
    }

    [Fact]
    public void Narrows24In32ToPackedInt24()
    {
        // 0x12345600 left-justified 24-in-32 → 0x123456.
        var input = new byte[] { 0x00, 0x56, 0x34, 0x12 };
        var output = new byte[3];
        PcmConverter.ToStorage(input, new AudioFormat(48_000, 1, 32, AudioSampleEncoding.Pcm, 24), output);

        Assert.Equal(new byte[] { 0x56, 0x34, 0x12 }, output);
    }

    [Fact]
    public void RejectsPartialFrames()
    {
        Assert.Throws<ArgumentException>(() => PcmConverter.ToStorage(new byte[7], AudioFormat.Float32Stereo48k, new byte[64]));
    }
}
