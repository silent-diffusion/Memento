using Memento.Audio.Capture;
using Memento.Audio.Writing;

namespace Memento.Audio.Tests.Capture;

public sealed class LevelMeterTests
{
    [Fact]
    public void FullScaleSineHasRmsOfOneOverRootTwo()
    {
        var reading = LevelMeter.Measure(Signals.Sine(48_000, 2, 1, 1_000, 1.0));

        Assert.Equal(1 / Math.Sqrt(2), reading.Rms, 3);
        Assert.Equal(1.0, reading.Peak, 3);
    }

    [Fact]
    public void ConstantSignalHasEqualRmsAndPeak()
    {
        var meter = new LevelMeter();
        meter.Add(Signals.AsBytes(Signals.Constant(480, 2, -0.25f)), AudioFormat.Float32Stereo48k);

        var reading = meter.Take();

        Assert.Equal(0.25f, reading.Rms, 5);
        Assert.Equal(0.25f, reading.Peak, 5);
    }

    [Fact]
    public void SilenceDilutesRmsButNotPeakAndTakeResets()
    {
        var meter = new LevelMeter();
        meter.Add(Signals.AsBytes(Signals.Constant(480, 2, 0.5f)), AudioFormat.Float32Stereo48k);
        meter.AddSilence(1_440, 2);

        var reading = meter.Take();
        Assert.Equal(0.25f, reading.Rms, 5);   // sqrt(0.25 * 1/4)
        Assert.Equal(0.5f, reading.Peak, 5);
        Assert.Equal(LevelReading.Silence, meter.Take());
    }

    [Fact]
    public void Int24InputIsScaledToFullScale()
    {
        var bytes = new byte[6];
        PcmConverter.WriteInt24(bytes, PcmConverter.Int24Min);
        PcmConverter.WriteInt24(bytes.AsSpan(3), 0);
        var meter = new LevelMeter();
        meter.Add(bytes, AudioFormat.Pcm24(48_000, 2));

        var reading = meter.Take();
        Assert.Equal(1f, reading.Peak, 5);
        Assert.Equal((float)Math.Sqrt(0.5), reading.Rms, 4);
    }

    [Fact]
    public void ValuesAreClampedToZeroOne()
    {
        var reading = LevelMeter.Measure([3f, -4f]);

        Assert.Equal(1f, reading.Peak);
        Assert.Equal(1f, reading.Rms);
    }
}
