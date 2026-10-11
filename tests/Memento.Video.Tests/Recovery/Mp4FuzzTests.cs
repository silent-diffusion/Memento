using System.Buffers.Binary;
using System.Globalization;
using Memento.Documents.Tests.Fuzz;
using Memento.Video.Recovery;
using Memento.Video.Tests.Export;
using Xunit.Abstractions;

namespace Memento.Video.Tests.Recovery;

/// <summary>
/// Seeded, deterministic fuzzing of what reads a video file from a project folder (security audit 2, SA2-01, SA2-02):
/// the box reader behind crash recovery and the video stage (<see cref="Mp4FileSummary"/>, <see cref="VideoRecovery.Repair"/>)
/// and the remuxer that joins parts (<see cref="Mp4Remuxer"/>, Media Foundation's MP4 parser). The inputs are the
/// recorder's own structure (<see cref="Mp4Builder"/>) and a real software-encoded MP4, mutated with the agenda harness's
/// <see cref="ByteMutator"/> and with box-size mutations (0, 1 with a huge 64-bit size, below the header, past the
/// end, a flood of empty boxes).
/// <para>
/// Each run must end in a result or the documented refusal (<see cref="InvalidDataException"/> from the reader,
/// <see cref="VideoCaptureException"/> from the remuxer); Repair may only cut bytes off the end of a fragmented file,
/// never change what it keeps; the remuxer never changes its input and leaves no temporary file. Work is measured, not
/// time: the reader's allocations per run are bounded. Before the audit's fixes a 64-bit size that overflowed moved the
/// reader backwards (ArgumentOutOfRangeException), a short mdhd/stts/tfhd/trun/tfdt threw IndexOutOfRange or
/// ArgumentOutOfRange (escaping crash recovery and stopping it for every other project), and a file of empty boxes grew
/// the box list without limit.
/// </para>
/// </summary>
public sealed class Mp4FuzzTests(ITestOutputHelper output) : IDisposable
{
    private const int ReaderIterations = 3_000;
    private const int RemuxIterations = 120;
    private const long MaxReaderAllocation = 96L << 20;

    private readonly string _folder = Directory.CreateTempSubdirectory("memento-mp4-fuzz-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void MutatedRecordingsAreReadOrRefusedAndRepairOnlyCutsTheTail()
    {
        byte[][] seeds =
        [
            Mp4Builder.File(fragments: 6, index: true),
            Mp4Builder.File(fragments: 4),
            [.. Mp4Builder.File(fragments: 3), .. Mp4Builder.Fragment(10).AsSpan(0, 700)],
        ];
        var random = new Random(20261011);
        var failures = new List<string>();
        var (read, refused, cut) = (0, 0, 0);
        var path = Path.Combine(_folder, "screen.mp4");
        for (var i = 0; i < ReaderIterations; i++)
        {
            var seed = seeds[i % seeds.Length];
            var (input, description) = random.Next(3) == 0 ? MutateBoxSize(seed, random) : ByteMutator.Mutate(seed, random);
            try
            {
                var before = GC.GetAllocatedBytesForCurrentThread();
                using (var stream = new MemoryStream(input, writable: false))
                {
                    Mp4FileSummary.Read(stream);
                }

                var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                if (allocated > MaxReaderAllocation)
                {
                    failures.Add(string.Create(CultureInfo.InvariantCulture, $"#{i} ({description}): reading allocated {allocated >> 20} MB"));
                }

                File.WriteAllBytes(path, input);
                var repair = VideoRecovery.Repair(path);
                var after = File.ReadAllBytes(path);
                if (after.Length > input.Length || !input.AsSpan(0, after.Length).SequenceEqual(after))
                {
                    failures.Add($"#{i} ({description}): Repair changed bytes it kept");
                }

                cut += after.Length < input.Length ? 1 : 0;
                read++;
                _ = repair;
            }
            catch (InvalidDataException)
            {
                refused++;
                if (File.Exists(path) && File.ReadAllBytes(path).Length != input.Length)
                {
                    failures.Add($"#{i} ({description}): a refused file was cut");
                }
            }
            catch (Exception e)
            {
                failures.Add($"#{i} ({description}): {e.GetType().Name}: {e.Message}");
            }
        }

        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{ReaderIterations} runs: {read} read ({cut} with a torn tail cut), {refused} refused, {failures.Count} failures"));
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures.Take(20)));
    }

    [Fact]
    public void AFloodOfEmptyBoxesIsRefusedWithoutFillingMemory()
    {
        // 2,000,001 top-level boxes of 8 bytes (16 MB): more than any recording has.
        var flood = new byte[(Mp4BoxReader.MaxTopLevelBoxes + 1) * 8];
        for (var at = 0; at < flood.Length; at += 8)
        {
            BinaryPrimitives.WriteUInt32BigEndian(flood.AsSpan(at), 8);
            "free"u8.CopyTo(flood.AsSpan(at + 4));
        }

        using var stream = new MemoryStream(flood, writable: false);
        Assert.Throws<InvalidDataException>(() => Mp4BoxReader.TopLevel(stream));

        // A moov holding 65,537 empty children.
        var children = new byte[(Mp4BoxReader.MaxChildren + 1) * 8];
        for (var at = 0; at < children.Length; at += 8)
        {
            BinaryPrimitives.WriteUInt32BigEndian(children.AsSpan(at), 8);
            "free"u8.CopyTo(children.AsSpan(at + 4));
        }

        var path = Path.Combine(_folder, "flood.mp4");
        byte[] file = [.. Mp4Builder.Box("ftyp", new byte[8]), .. Mp4Builder.Box("moov", children)];
        File.WriteAllBytes(path, file);
        Assert.Throws<InvalidDataException>(() => VideoRecovery.Repair(path));
        Assert.Equal(file.Length, new FileInfo(path).Length);
    }

    [Theory]
    [InlineData(0x7FFF_FFFF_FFFF_FFF0UL)]
    [InlineData(0xFFFF_FFFF_FFFF_FFFFUL)]
    [InlineData(0x8000_0000_0000_0000UL)]
    [InlineData(4UL)]
    public void AHostile64BitSizeInsideTheMovieIsStoppedAtNotFollowed(ulong size)
    {
        var big = new byte[16 + 8];
        BinaryPrimitives.WriteUInt32BigEndian(big, 1);
        "free"u8.CopyTo(big.AsSpan(4));
        BinaryPrimitives.WriteUInt64BigEndian(big.AsSpan(8), size);
        byte[] file = [.. Mp4Builder.Box("ftyp", new byte[8]), .. Mp4Builder.Box("moov", big, Mp4Builder.Box("mvex")), .. Mp4Builder.Fragment(10)];
        using var stream = new MemoryStream(file, writable: false);

        var summary = Mp4FileSummary.Read(stream);

        Assert.Single(summary.Fragments);
        Assert.Equal(0, summary.TornBytes);
    }

    [Fact]
    public async Task MutatedMp4sAreRemuxedOrRefusedAndNeverChanged()
    {
        var source = SyntheticMedia.Video(Path.Combine(_folder, "seed.mp4"), 64, 64, 15, 12, i => (byte)(16 + (i * 10)));
        var seed = await File.ReadAllBytesAsync(source);
        var random = new Random(1011);
        var failures = new List<string>();
        var (remuxed, refused) = (0, 0);
        for (var i = 0; i < RemuxIterations; i++)
        {
            var (input, description) = random.Next(3) == 0 ? MutateBoxSize(seed, random) : ByteMutator.Mutate(seed, random);
            var part = Path.Combine(_folder, $"part-{i}.mp4");
            var joined = Path.Combine(_folder, $"joined-{i}.mp4");
            await File.WriteAllBytesAsync(part, input);
            try
            {
                using var limit = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                await Mp4Remuxer.RemuxAsync([new Mp4Remuxer.Part(part, 0)], joined, limit.Token).WaitAsync(limit.Token);
                remuxed++;
            }
            catch (VideoCaptureException)
            {
                refused++;
                if (File.Exists(joined))
                {
                    failures.Add($"#{i} ({description}): a refused remux left its output");
                }
            }
            catch (Exception e)
            {
                failures.Add($"#{i} ({description}): {e.GetType().Name}: {e.Message}");
            }

            var kept = await File.ReadAllBytesAsync(part);
            if (!input.AsSpan().SequenceEqual(kept))
            {
                failures.Add($"#{i} ({description}): the input was changed");
            }

            if (File.Exists(joined + ".tmp"))
            {
                failures.Add($"#{i} ({description}): the temporary file was left behind");
            }

            File.Delete(part);
            File.Delete(joined);
        }

        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{RemuxIterations} runs: {remuxed} remuxed, {refused} refused, {failures.Count} failures"));
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures.Take(20)));
    }

    /// <summary>Overwrites one box header's size (found by walking the structure) with a hostile value.</summary>
    private static (byte[] Bytes, string Description) MutateBoxSize(byte[] input, Random random)
    {
        var headers = new List<int>();
        Walk(input, 0, input.Length, 0, headers);
        if (headers.Count == 0)
        {
            return ByteMutator.Mutate(input, random);
        }

        var bytes = (byte[])input.Clone();
        var at = headers[random.Next(headers.Count)];
        switch (random.Next(5))
        {
            case 0:
                BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(at), 0);
                return (bytes, $"size 0 at {at}");
            case 1:
                BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(at), (uint)random.Next(1, 8));
                return (bytes, $"size below the header at {at}");
            case 2:
                BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(at), uint.MaxValue - (uint)random.Next(16));
                return (bytes, $"size past the end at {at}");
            case 3:
            {
                // size 1 and a 64-bit size over the first 8 payload bytes.
                BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(at), 1);
                if (at + 16 <= bytes.Length)
                {
                    ulong[] values = [ulong.MaxValue, long.MaxValue, (ulong)long.MaxValue - 8, 0x8000_0000_0000_0000UL, 0, 15];
                    BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(at + 8), values[random.Next(values.Length)]);
                }

                return (bytes, $"64-bit size at {at}");
            }

            default:
            {
                var size = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(at));
                var delta = random.Next(-64, 65);
                BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(at), (uint)Math.Max(0, size + delta));
                return (bytes, $"size {delta:+0;-0} at {at}");
            }
        }
    }

    private static void Walk(byte[] bytes, int start, int end, int depth, List<int> headers)
    {
        var offset = start;
        while (offset + 8 <= end && depth < 8)
        {
            var size = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset));
            if (size < 8 || offset + (long)size > end)
            {
                return;
            }

            headers.Add(offset);
            var type = System.Text.Encoding.ASCII.GetString(bytes, offset + 4, 4);
            if (type is "moov" or "trak" or "mdia" or "minf" or "stbl" or "mvex" or "moof" or "traf" or "mfra")
            {
                Walk(bytes, offset + 8, offset + (int)size, depth + 1, headers);
            }

            offset += (int)size;
        }
    }
}
