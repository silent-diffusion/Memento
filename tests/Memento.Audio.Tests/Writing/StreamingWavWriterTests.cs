using System.Buffers.Binary;
using Memento.Audio.Writing;
using NAudio.Wave;

namespace Memento.Audio.Tests.Writing;

public sealed class StreamingWavWriterTests : IDisposable
{
    private static readonly AudioFormat Int24Stereo = AudioFormat.Pcm24(48_000, 2);
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void WritesHeaderFirstWithExtensibleInt24Format()
    {
        var path = _dir.File("a.wav");
        var data = Signals.Int24Sweep(48_000, 2, 0.25);
        using (var writer = new StreamingWavWriter(path, Int24Stereo, durableCheckpoints: false))
        {
            writer.Write(data);
            Assert.Equal(12_000, writer.Frames);
        }

        var bytes = File.ReadAllBytes(path);
        Assert.Equal("RIFF"u8.ToArray(), bytes[..4]);
        Assert.Equal(bytes.Length - 8, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4)));
        Assert.Equal("WAVE"u8.ToArray(), bytes[8..12]);
        Assert.Equal("fmt "u8.ToArray(), bytes[12..16]);
        Assert.Equal(40, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(16)));
        Assert.Equal(0xFFFE, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(20)));
        Assert.Equal(2, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(22)));
        Assert.Equal(48_000, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(24)));
        Assert.Equal(288_000, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(28)));
        Assert.Equal(6, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(32)));
        Assert.Equal(24, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(34)));
        Assert.Equal("data"u8.ToArray(), bytes[60..64]);
        Assert.Equal(data.Length, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(64)));
        Assert.Equal(68 + data.Length, bytes.Length);
        Assert.Equal(data, bytes[68..]);
    }

    [Fact]
    public void PlainPcmHeaderForSixteenBitStereoIsReadableByNAudio()
    {
        var path = _dir.File("b.wav");
        var format = AudioFormat.Pcm16(44_100, 2);
        var data = new byte[44_100 * 4];
        new Random(7).NextBytes(data);
        Signals.WriteWav(path, format, data);

        Assert.Equal(44 + data.Length, new FileInfo(path).Length);
        using var reader = new WaveFileReader(path);
        Assert.Equal(WaveFormatEncoding.Pcm, reader.WaveFormat.Encoding);
        Assert.Equal(44_100, reader.Length / reader.WaveFormat.BlockAlign);
        var back = new byte[data.Length];
        Assert.Equal(data.Length, reader.Read(back, 0, back.Length));
        Assert.Equal(data, back);
    }

    [Fact]
    public void ExtensibleInt24IsReadableByNAudio()
    {
        var path = _dir.File("c.wav");
        var data = Signals.Int24Sweep(48_000, 2, 0.5);
        Signals.WriteWav(path, Int24Stereo, data);

        using var reader = new WaveFileReader(path);
        Assert.Equal(24, reader.WaveFormat.BitsPerSample);
        Assert.Equal(24_000, reader.Length / reader.WaveFormat.BlockAlign);
    }

    [Fact]
    public void HeaderSizesAreZeroUntilTheFirstCheckpoint()
    {
        var path = _dir.File("d.wav");
        using var writer = new StreamingWavWriter(path, Int24Stereo, durableCheckpoints: false);
        writer.Write(Signals.Int24Sweep(48_000, 2, 1));
        writer.Flush();

        var (riff, data) = ReadSizes(path);
        Assert.Equal(0u, data);
        Assert.Equal(0u, riff);
    }

    [Fact]
    public void CheckpointPatchesRiffAndDataSizesWhileTheFileStaysOpen()
    {
        var path = _dir.File("e.wav");
        using var writer = new StreamingWavWriter(path, Int24Stereo, durableCheckpoints: false);
        var second = Signals.Int24Sweep(48_000, 2, 1);
        writer.Write(second);
        writer.Checkpoint();

        var (riff, data) = ReadSizes(path);
        Assert.Equal((uint)second.Length, data);
        Assert.Equal((uint)(new FileInfo(path).Length - 8), riff);

        writer.Write(second);
        writer.Checkpoint();
        (_, data) = ReadSizes(path);
        Assert.Equal((uint)(2 * second.Length), data);
        Assert.Equal(TimeSpan.FromSeconds(2), writer.Duration);
    }

    [Fact]
    public void RefusesToOverwriteAnExistingFile()
    {
        var path = _dir.File("f.wav");
        File.WriteAllBytes(path, [1, 2, 3]);

        Assert.Throws<IOException>(() => new StreamingWavWriter(path, Int24Stereo));
        Assert.Equal(3, new FileInfo(path).Length);
    }

    [Fact]
    public void RepairAfterAnAbandonedStreamRecoversEverythingHandedToTheOs()
    {
        var path = _dir.File("crash.wav");
        var writer = new StreamingWavWriter(path, Int24Stereo, durableCheckpoints: false);
        var oneSecond = Signals.Int24Sweep(48_000, 2, 1);
        writer.Write(oneSecond);
        writer.Checkpoint();
        writer.Write(oneSecond);
        writer.Flush();                       // the once-per-second flush
        writer.Write(oneSecond.AsSpan(0, 6_000)); // 1000 frames still in the managed buffer
        writer.Abandon();                     // process dies: buffer lost, header says 1 s

        var before = WavFileInfo.Read(path);
        Assert.True(before.HeaderNeedsRepair);
        Assert.Equal(oneSecond.Length, before.DeclaredDataBytes);

        var result = StreamingWavWriter.Repair(path);

        Assert.Equal(oneSecond.Length, result.DeclaredDataBytes);
        Assert.Equal(96_000, result.Frames);
        Assert.Equal(TimeSpan.FromSeconds(2), result.Duration);
        Assert.Equal(0, result.TruncatedBytes);
        var after = WavFileInfo.Read(path);
        Assert.False(after.HeaderNeedsRepair);
        Assert.Equal(96_000, after.Frames);
        using var reader = new WaveFileReader(path);
        Assert.Equal(96_000, reader.Length / reader.WaveFormat.BlockAlign);
    }

    [Fact]
    public void RepairTruncatesATrailingPartialFrame()
    {
        var path = _dir.File("partial.wav");
        Signals.WriteWav(path, Int24Stereo, Signals.Int24Sweep(48_000, 2, 0.1));
        using (var f = new FileStream(path, FileMode.Append))
        {
            f.Write([9, 9, 9, 9]);
        }

        var result = StreamingWavWriter.Repair(path);

        Assert.Equal(4, result.TruncatedBytes);
        Assert.Equal(4_800, result.Frames);
        Assert.Equal(68 + (4_800 * 6), new FileInfo(path).Length);
    }

    [Fact]
    public void RepairOfANeverCheckpointedFileUsesTheFileLength()
    {
        var path = _dir.File("never.wav");
        var writer = new StreamingWavWriter(path, AudioFormat.Pcm16(16_000, 1), bufferSize: 1024, durableCheckpoints: false);
        writer.Write(new byte[32_000]);
        writer.Flush();
        writer.Abandon();

        var result = StreamingWavWriter.Repair(path);

        Assert.Equal(0, result.DeclaredDataBytes);
        Assert.Equal(16_000, result.Frames);
    }

    [Fact]
    public void RepairNamesTheFileWhenThereIsNoHeader()
    {
        var path = _dir.File("empty.wav");
        File.WriteAllBytes(path, []);

        var ex = Assert.Throws<InvalidDataException>(() => StreamingWavWriter.Repair(path));
        Assert.Contains("empty.wav", ex.Message, StringComparison.Ordinal);
    }

    private static (uint Riff, uint Data) ReadSizes(string path)
    {
        using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var head = new byte[68];
        f.ReadExactly(head);
        return (BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan(4)), BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan(64)));
    }
}
