using Memento.Audio.Writing;

namespace Memento.Audio.Tests.Writing;

public sealed class RollingWavWriterTests : IDisposable
{
    private static readonly AudioFormat Format = AudioFormat.Pcm24(48_000, 2);
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void RollsOverToNumberedPartsAtTheThresholdOnFrameBoundaries()
    {
        var data = Signals.Int24Sweep(48_000, 2, 0.25); // 12 000 frames
        var rolled = new List<string>();
        using (var writer = new RollingWavWriter(_dir.Path, "mic", Format, rolloverBytes: (5_000 * 6) + 4, durableCheckpoints: false))
        {
            writer.RolledOver += (_, closed) => rolled.Add(Path.GetFileName(closed));
            writer.Write(data);
            Assert.Equal(12_000, writer.Frames);
            Assert.Equal(30_000, writer.RolloverBytes);
        }

        Assert.Equal(["mic.wav", "mic.part2.wav"], rolled);
        var set = WavTrackSet.Open(_dir.Path, "mic");
        Assert.Equal(["mic.wav", "mic.part2.wav", "mic.part3.wav"], set.Parts.Select(p => Path.GetFileName(p.Path)));
        Assert.Equal([5_000L, 5_000L, 2_000L], set.Parts.Select(p => p.Frames));
        Assert.All(set.Parts, p => Assert.False(p.HeaderNeedsRepair));
        Assert.Equal(12_000, set.TotalFrames);
        Assert.Equal(TimeSpan.FromMilliseconds(250), set.Duration);

        using var reader = set.OpenReader();
        var back = new byte[data.Length + 100];
        var n = reader.Read(back);
        Assert.Equal(data.Length, n);
        Assert.Equal(data, back[..n]);
    }

    [Fact]
    public void SilenceAlsoRollsOver()
    {
        using (var writer = new RollingWavWriter(_dir.Path, "system", Format, rolloverBytes: 6_000, durableCheckpoints: false))
        {
            writer.WriteSilence(2_500);
        }

        var set = WavTrackSet.Open(_dir.Path, "system");
        Assert.Equal([1_000L, 1_000L, 500L], set.Parts.Select(p => p.Frames));
    }

    [Fact]
    public void FindPartsStopsAtTheFirstMissingPart()
    {
        File.WriteAllBytes(_dir.File("x.wav"), []);
        File.WriteAllBytes(_dir.File("x.part2.wav"), []);
        File.WriteAllBytes(_dir.File("x.part4.wav"), []);

        Assert.Equal(2, WavTrackSet.FindParts(_dir.Path, "x").Count);
        Assert.Empty(WavTrackSet.FindParts(_dir.Path, "y"));
    }

    [Fact]
    public void PartsWithDifferentFormatsAreRejectedByName()
    {
        Signals.WriteWav(_dir.File("t.wav"), Format, new byte[600]);
        Signals.WriteWav(_dir.File("t.part2.wav"), AudioFormat.Pcm16(48_000, 2), new byte[400]);

        var ex = Assert.Throws<InvalidDataException>(() => WavTrackSet.Open(_dir.Path, "t"));
        Assert.Contains("t.part2.wav", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnpatchedPartIsReadUpToItsLength()
    {
        var path = _dir.File("u.wav");
        var writer = new StreamingWavWriter(path, Format, durableCheckpoints: false);
        writer.Write(new byte[6 * 1000]);
        writer.Flush();
        writer.Abandon();

        var info = WavFileInfo.Read(path);
        Assert.True(info.HeaderNeedsRepair);
        Assert.Equal(1_000, info.Frames);
    }
}
