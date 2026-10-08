using Memento.Transcription.Audio;
using Memento.Transcription.Windows;
using Memento.Transcription.Words;

namespace Memento.Transcription.Tests;

public sealed class SpeechPackerTests
{
    private const int Rate = 16_000;

    /// <summary>A window of <paramref name="seconds"/> s where sample i holds its own time (i / rate), so packed audio shows where it came from.</summary>
    private static float[] Clock(double seconds) => Enumerable.Range(0, (int)(seconds * Rate)).Select(i => (float)(i / (double)Rate)).ToArray();

    [Fact]
    public void LongSilencesAreShortenedToThePadsAroundTheSound()
    {
        // Sound at 2–5 s and 30–31 s of a 40 s window that starts at 100 s on the track.
        var chunks = SpeechPacker.Pack(Clock(40), Rate, 100, [(102, 105), (130, 131)]);

        var chunk = Assert.Single(chunks);
        Assert.Equal([new SpeechPiece(1.6, 5.4, 0), new SpeechPiece(29.6, 31.4, 3.8)], chunk.Pieces);
        Assert.Equal(5.6, chunk.Seconds, 6);
        Assert.Equal((int)(5.6 * Rate), chunk.Samples.Length);
        Assert.Equal(1.6f, chunk.Samples[0], 4);
        Assert.Equal(29.6f, chunk.Samples[(int)(3.8 * Rate)], 4);
    }

    [Fact]
    public void ShortPausesStayAsTheyAre()
    {
        var chunk = Assert.Single(SpeechPacker.Pack(Clock(10), Rate, 0, [(1, 2), (2.7, 4)]));

        Assert.Equal([new SpeechPiece(0.6, 4.4, 0)], chunk.Pieces);
    }

    [Fact]
    public void SoundAtTheWindowEdgesIsClippedToTheWindow()
    {
        var chunk = Assert.Single(SpeechPacker.Pack(Clock(10), Rate, 50, [(45, 50.2), (59.9, 70)]));

        Assert.Equal(0, chunk.Pieces[0].SourceStart);
        Assert.Equal(10, chunk.Pieces[^1].SourceEnd, 6);
    }

    [Fact]
    public void AWindowWithNothingLoudEnoughGoesToTheEngineWhole()
    {
        var samples = Clock(12);

        var chunk = Assert.Single(SpeechPacker.Pack(samples, Rate, 0, []));

        Assert.Same(samples, chunk.Samples);
        Assert.Equal([new SpeechPiece(0, 12, 0)], chunk.Pieces);
    }

    [Fact]
    public void ChunksStayUnderTheEnginesWindowAndAreCutBetweenSounds()
    {
        // Ten 5-second sounds 10 s apart: each piece is 5.8 s, so four fit in a 28 s chunk.
        var regions = Enumerable.Range(0, 10).Select(i => (Start: 2.0 + (10 * i), End: 7.0 + (10 * i))).ToList();

        var chunks = SpeechPacker.Pack(Clock(100), Rate, 0, regions);

        Assert.Equal([4, 4, 2], chunks.Select(c => c.Pieces.Count));
        Assert.All(chunks, c => Assert.True(c.Seconds <= SpeechPacker.ChunkSeconds));
        Assert.Equal(regions.Count, chunks.Sum(c => c.Pieces.Count));
        Assert.Equal(41.6, chunks[1].Pieces[0].SourceStart, 6);
        Assert.Equal(0, chunks[1].Pieces[0].PackedStart);
    }

    [Fact]
    public void SoundLongerThanAChunkIsCutAtItsQuietestMoment()
    {
        // 70 s of loud sound with one quiet moment at 20 s.
        var samples = Enumerable.Range(0, 70 * Rate).Select(i => i is >= 20 * Rate and < (20 * Rate) + 1600 ? 0.001f : (float)(0.5 * Math.Sin(i * 0.1))).ToArray();

        var chunks = SpeechPacker.Pack(samples, Rate, 0, [(0, 70)]);

        Assert.True(chunks.Count >= 3);
        Assert.InRange(chunks[0].Pieces[^1].SourceEnd, 20, 20.1);
        Assert.All(chunks, c => Assert.True(c.Seconds <= SpeechPacker.ChunkSeconds + 1e-9));
        Assert.Equal(70, chunks.Sum(c => c.Seconds), 3);
        for (var i = 1; i < chunks.Count; i++)
        {
            Assert.Equal(chunks[i - 1].Pieces[^1].SourceEnd, chunks[i].Pieces[0].SourceStart, 6);
        }
    }

    [Fact]
    public void PackedTimesMapBackToTheWindowAndBordersBelongToTheRightSide()
    {
        var chunk = Assert.Single(SpeechPacker.Pack(Clock(40), Rate, 0, [(2, 5), (30, 31)]));

        Assert.Equal(1.6, chunk.ToWindowStart(0), 6);
        Assert.Equal(3.6, chunk.ToWindowStart(2), 6);
        // 3.8 s is where the first piece ends and the second begins.
        Assert.Equal(29.6, chunk.ToWindowStart(3.8), 6);
        Assert.Equal(5.4, chunk.ToWindowEnd(3.8), 6);
        Assert.Equal(30.6, chunk.ToWindowEnd(4.8), 6);
        Assert.Equal(31.4, chunk.ToWindowEnd(99), 6);
    }

    [Fact]
    public void AnEngineSegmentAndItsTokensComeBackOnTheWindow()
    {
        var chunk = Assert.Single(SpeechPacker.Pack(Clock(40), Rate, 0, [(2, 5), (30, 31)]));
        var raw = new RawSegment(3.0, 4.6, " Next item.", 0.9, [new TokenInfo(" Next", 3.0, 3.6, 0.9), new TokenInfo(" item.", 4.0, 4.6, 0.8)]);

        var segment = WordBuilder.ToSegment(chunk.ToWindow(raw), 100, keepWords: true);

        Assert.Equal(104.6, segment.Start, 3);
        Assert.Equal(130.4, segment.End, 3);
        Assert.Equal([104.6, 129.8], segment.Words.Select(w => w.S));
        Assert.Equal([105.2, 130.4], segment.Words.Select(w => w.E));
    }

    [Fact]
    public void RegionEdgesAreRefinedToTenMilliseconds()
    {
        var energy = new SpeechEnergy();
        energy.Add(new float[(int)(1.234 * Rate)]);
        energy.Add(Enumerable.Range(0, (int)(2.5 * Rate)).Select(i => (float)(0.2 * Math.Sin(2 * Math.PI * 220 * i / Rate))).ToArray());
        energy.Add(new float[2 * Rate]);

        var region = Assert.Single(energy.Regions());
        var sound = Assert.Single(energy.SoundRegions());

        Assert.InRange(region.Start, 1.22, 1.24);
        Assert.InRange(region.End, 3.73, 3.75);
        Assert.Equal(region, sound);
    }

    [Fact]
    public void QuietSpeechIsSoundButNotSpeech()
    {
        var energy = new SpeechEnergy();
        energy.Add(new float[2 * Rate]);
        energy.Add(Enumerable.Range(0, 2 * Rate).Select(i => (float)(0.008 * Math.Sin(2 * Math.PI * 220 * i / Rate))).ToArray()); // about -45 dBFS
        energy.Add(new float[2 * Rate]);

        Assert.Empty(energy.Regions());
        var sound = Assert.Single(energy.SoundRegions());
        Assert.InRange(sound.Start, 1.99, 2.01);
    }
}
