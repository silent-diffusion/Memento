using Memento.Video.Recovery;

namespace Memento.Video.Tests.Recovery;

public sealed class Mp4FileSummaryTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "memento-mp4-tests-" + Guid.NewGuid().ToString("N"));

    public Mp4FileSummaryTests()
    {
        Directory.CreateDirectory(_folder);
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void ACleanFragmentedFileIsReadFragmentByFragment()
    {
        using var stream = new MemoryStream(Mp4Builder.File(fragments: 6, index: true));

        var summary = Mp4FileSummary.Read(stream);

        Assert.True(summary.IsFragmented);
        Assert.True(summary.HasMoov);
        Assert.True(summary.HasIndex);
        Assert.Equal(Mp4Builder.Timescale, summary.Timescale);
        Assert.Equal(6, summary.Fragments.Count);
        Assert.Equal(60, summary.Samples);
        Assert.Equal(2.0, summary.DurationSeconds, 3);
        Assert.Equal(0, summary.TornBytes);
        Assert.Equal(stream.Length, summary.ValidLength);
    }

    [Fact]
    public void WithoutTfdtEachFragmentStartsWhereThePreviousEnded()
    {
        using var stream = new MemoryStream(Mp4Builder.File(fragments: 3));

        var summary = Mp4FileSummary.Read(stream);

        Assert.Equal([0L, 10_000L, 20_000L], summary.Fragments.Select(f => f.DecodeTime));
    }

    [Fact]
    public void ALongFrameAfterAGapCountsInTheDuration()
    {
        var bytes = new List<byte>(Mp4Builder.Header());
        bytes.AddRange(Mp4Builder.Fragment(10));
        bytes.AddRange(Mp4Builder.Fragment(1, duration: 90_000)); // one frame held for 3 s (a minimised window)
        using var stream = new MemoryStream([.. bytes]);

        var summary = Mp4FileSummary.Read(stream);

        Assert.Equal(11, summary.Samples);
        Assert.Equal(10_000.0 / 30_000 + 3, summary.DurationSeconds, 3);
    }

    [Fact]
    public void AWriterThatDiedMidFragmentLeavesATornTailThatIsReported()
    {
        var whole = Mp4Builder.File(fragments: 5);
        var torn = whole.Concat(Mp4Builder.Fragment(10).Take(700)).ToArray();
        using var stream = new MemoryStream(torn);

        var summary = Mp4FileSummary.Read(stream);

        Assert.Equal(5, summary.Fragments.Count);
        Assert.Equal(whole.Length, summary.ValidLength);
        Assert.Equal(700, summary.TornBytes);
        Assert.False(summary.HasIndex);
    }

    [Fact]
    public void AMoofWithoutItsMdatIsPartOfTheTornTail()
    {
        var whole = Mp4Builder.File(fragments: 2);
        var fragment = Mp4Builder.Fragment(10);
        var moofLength = fragment.Length - (8 + 1000);
        var torn = whole.Concat(fragment.Take(moofLength)).ToArray();
        using var stream = new MemoryStream(torn);

        var summary = Mp4FileSummary.Read(stream);

        Assert.Equal(2, summary.Fragments.Count);
        Assert.Equal(moofLength, summary.TornBytes);
    }

    [Fact]
    public void RepairCutsOnlyTheTornTail()
    {
        var path = Path.Combine(_folder, "screen.mp4");
        var whole = Mp4Builder.File(fragments: 4);
        File.WriteAllBytes(path, [.. whole, .. Mp4Builder.Fragment(10).Take(333)]);

        var result = VideoRecovery.Repair(path);

        Assert.True(result.Readable);
        Assert.Equal(4, result.Fragments);
        Assert.Equal(333, result.TornBytesRemoved);
        Assert.Equal(whole, File.ReadAllBytes(path));
        Assert.Equal(40, result.Samples);
    }

    [Fact]
    public void RepairLeavesAWholeFileUntouched()
    {
        var path = Path.Combine(_folder, "whole.mp4");
        var whole = Mp4Builder.File(fragments: 3, index: true);
        File.WriteAllBytes(path, whole);

        var result = VideoRecovery.Repair(path);

        Assert.Equal(0, result.TornBytesRemoved);
        Assert.Equal(whole, File.ReadAllBytes(path));
    }

    [Fact]
    public void AFileWithoutAHeaderIsNotReadable()
    {
        var path = Path.Combine(_folder, "empty.mp4");
        File.WriteAllBytes(path, new byte[5]);

        var result = VideoRecovery.Repair(path);

        Assert.False(result.Readable);
        Assert.Equal(0, result.Fragments);
    }

    [Fact]
    public void ALargeSizeBoxIsRead()
    {
        // size == 1: a 64-bit size follows the type.
        var box = new byte[16 + 4];
        box[3] = 1;
        "free"u8.CopyTo(box.AsSpan(4));
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(box.AsSpan(8), (ulong)box.Length);
        using var stream = new MemoryStream(box);

        var (boxes, torn) = Mp4BoxReader.TopLevel(stream);

        Assert.Single(boxes);
        Assert.Equal(16, boxes[0].HeaderSize);
        Assert.Null(torn);
    }
}
