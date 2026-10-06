using System.Buffers.Binary;
using Memento.Core.Audio;
using Memento.Core.Tests.Fakes;

namespace Memento.Core.Tests.Audio;

public sealed class StreamingWavWriterTests : IDisposable
{
    private static readonly PcmFormat Mono = PcmFormat.Pcm16(48_000, 1);

    private readonly TempDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    [Fact]
    public void WritesAWavThatReadsBackExactly()
    {
        var path = _directory.File("a.wav");
        WavTestFiles.Write(path, Mono, 4800, (f, _) => (f % 100) / 200f);

        var info = WavInfo.Read(path);
        var samples = WavTestFiles.ReadAll(path);

        Assert.Equal(Mono, info.Format);
        Assert.Equal(80, info.DataOffset);
        Assert.Equal(9600, info.DeclaredDataBytes);
        Assert.Equal(100, info.DurationMs);
        Assert.Equal(4800, samples.Length);
        Assert.Equal(0.25f, samples[50], 3);
        Assert.Equal(80 + 9600, new FileInfo(path).Length);
    }

    [Fact]
    public void CheckpointMakesTheFileValidWhileStillWriting()
    {
        var path = _directory.File("live.wav");
        using var writer = new StreamingWavWriter(path, Mono);
        writer.Write(WavTestFiles.Pcm16(Mono, 2400, (_, _) => 0.1f));
        writer.Checkpoint();

        // Another reader (recovery, the UI) sees exactly the checkpointed audio.
        var info = WavInfo.Read(path);
        Assert.Equal(4800, info.DeclaredDataBytes);
        Assert.Equal(4800, writer.CheckpointedBytes);
        Assert.Equal(50, info.DurationMs);

        writer.Write(WavTestFiles.Pcm16(Mono, 2400, (_, _) => 0.1f));
        Assert.Equal(4800, WavInfo.Read(path).DeclaredDataBytes);
        writer.Checkpoint();
        Assert.Equal(9600, WavInfo.Read(path).DeclaredDataBytes);
    }

    [Fact]
    public void AnAbandonedStreamIsRepairedFromItsLength()
    {
        var path = _directory.File("crashed.wav");
        var writer = new StreamingWavWriter(path, Mono);
        writer.Write(WavTestFiles.Pcm16(Mono, 48_000, (_, _) => 0.2f));
        writer.Checkpoint();
        writer.Write(WavTestFiles.Pcm16(Mono, 24_000, (_, _) => 0.2f));
        writer.Abandon(flushBufferedSamples: true);

        Assert.Equal(96_000, WavInfo.Read(path).DeclaredDataBytes);

        var result = WavRepair.Repair(path);

        Assert.True(result.Succeeded);
        Assert.True(result.Changed);
        Assert.Equal(144_000, result.DataBytes);
        Assert.Equal(1500, result.DurationMs);
        Assert.Equal(144_000, WavInfo.Read(path).DeclaredDataBytes);
        Assert.Equal(72_000, WavTestFiles.ReadAll(path).Length);
    }

    [Fact]
    public void AbandoningWithoutFlushLosesOnlyTheInMemoryBuffer()
    {
        var path = _directory.File("killed.wav");
        var writer = new StreamingWavWriter(path, Mono);
        writer.Write(WavTestFiles.Pcm16(Mono, 1000, (_, _) => 0.2f));
        writer.Abandon(flushBufferedSamples: false);

        var result = WavRepair.Repair(path);

        // The 2000 bytes never left process memory (the writer's 64 KB buffer), as after a kill.
        Assert.True(result.Succeeded);
        Assert.Equal(0, result.DataBytes);
        Assert.Equal(80, new FileInfo(path).Length);
    }

    [Fact]
    public void RepairCutsATrailingPartialFrame()
    {
        var path = _directory.File("odd.wav");
        var stereo = PcmFormat.Pcm16(48_000, 2);
        var writer = new StreamingWavWriter(path, stereo);
        writer.Write(WavTestFiles.Pcm16(stereo, 100, (_, _) => 0.1f));
        writer.Write(new byte[3]);
        writer.Abandon(flushBufferedSamples: true);

        var result = WavRepair.Repair(path);

        Assert.Equal(400, result.DataBytes);
        Assert.Equal(3, result.TruncatedBytes);
        Assert.Equal(480, new FileInfo(path).Length);
    }

    [Fact]
    public void RepairLeavesAValidFileUnchanged()
    {
        var path = _directory.File("ok.wav");
        WavTestFiles.Write(path, Mono, 480, (_, _) => 0.3f);
        var before = File.ReadAllBytes(path);

        var result = WavRepair.Repair(path);

        Assert.True(result.Succeeded);
        Assert.False(result.Changed);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void RepairRebuildsADestroyedHeaderWhenTheFormatIsKnown()
    {
        var path = _directory.File("garbled.wav");
        WavTestFiles.Write(path, Mono, 480, (_, _) => 0.3f);
        using (var stream = File.OpenWrite(path))
        {
            stream.Write(new byte[44]);
        }

        Assert.False(WavRepair.Repair(path).Succeeded);

        var result = WavRepair.Repair(path, Mono);

        Assert.True(result.Succeeded);
        Assert.Equal(960, result.DataBytes);
        Assert.Equal(0.3f, WavTestFiles.ReadAll(path)[100], 3);
    }

    [Fact]
    public void RepairReportsAMissingFile()
    {
        var result = WavRepair.Repair(_directory.File("nothing.wav"));

        Assert.False(result.Succeeded);
        Assert.Equal("The track file is missing.", result.Problem);
    }

    [Fact]
    public void RepairPatchesACanonical44ByteHeader()
    {
        // A WAV from another tool: RIFF, fmt (16), data. Sizes left at zero.
        var path = _directory.File("foreign.wav");
        var header = new byte[44];
        "RIFF"u8.CopyTo(header);
        "WAVE"u8.CopyTo(header.AsSpan(8));
        "fmt "u8.CopyTo(header.AsSpan(12));
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(16), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(20), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(22), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(24), 48_000);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(28), 96_000);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(32), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(34), 16);
        "data"u8.CopyTo(header.AsSpan(36));
        File.WriteAllBytes(path, [.. header, .. new byte[200]]);

        var result = WavRepair.Repair(path);

        Assert.Equal(200, result.DataBytes);
        Assert.Equal(44, WavInfo.Read(path).DataOffset);
        Assert.Equal(200, WavInfo.Read(path).DeclaredDataBytes);
        Assert.Equal(236u, BinaryPrimitives.ReadUInt32LittleEndian(File.ReadAllBytes(path).AsSpan(4)));
    }

    [Fact]
    public void HeadersPast4GiBBecomeRf64()
    {
        var format = PcmFormat.IeeeFloat32(48_000, 2);
        const long dataBytes = 5_500_000_000;
        var header = WavLayout.BuildHeader(format, dataBytes);
        using var stream = new MemoryStream();
        stream.Write(header);

        var info = WavInfo.Read(stream);

        Assert.True(info.IsRf64);
        Assert.Equal(dataBytes, info.DeclaredDataBytes);
        Assert.Equal(format, info.Format);
        Assert.Equal("RF64", System.Text.Encoding.ASCII.GetString(header, 0, 4));
        Assert.Equal(uint.MaxValue, BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4)));
    }

    [Fact]
    public void FloatSamplesRoundTrip()
    {
        var path = _directory.File("float.wav");
        var format = PcmFormat.IeeeFloat32(44_100, 1);
        var bytes = new byte[400];
        for (var i = 0; i < 100; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * 4), i / 100f);
        }

        using (var writer = new StreamingWavWriter(path, format))
        {
            writer.Write(bytes);
        }

        var samples = WavTestFiles.ReadAll(path);
        Assert.Equal(format, WavInfo.Read(path).Format);
        Assert.Equal(0.42f, samples[42]);
    }

    [Fact]
    public void DiskFullIsRecognised()
    {
        Assert.True(DiskErrors.IsDiskFull(DiskErrors.CreateDiskFull("x.wav")));
        Assert.True(DiskErrors.IsDiskFull(new IOException("full", unchecked((int)0x80070027))));
        Assert.False(DiskErrors.IsDiskFull(new IOException("other", unchecked((int)0x80070005))));
    }
}
