using System.Globalization;
using System.Text;
using System.Text.Json;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Projects;
using Memento.Core.Tests.Fakes;
using Xunit.Abstractions;

namespace Memento.Core.Tests.Projects;

/// <summary>
/// The bridge reads <c>project.json</c> while stages write it. A write must never be dropped because a reader has the
/// file open, and a reader must only ever see a complete document (the old one or the new one).
/// </summary>
public sealed class AtomicJsonFileTests : IDisposable
{
    private const int Writes = 300;
    private const int PaddingLength = 4096;

    private readonly TempDirectory _directory = new();
    private readonly ITestOutputHelper _output;

    public AtomicJsonFileTests(ITestOutputHelper output) => _output = output;

    public void Dispose() => _directory.Dispose();

    [Fact]
    public async Task AReaderHoldingTheFileWithShareDeleteDropsNoWritesAndKeepsTheOldDocument()
    {
        var path = _directory.File("project.json");
        await WriteAsync(path, -1);

        int dropped;
        using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            dropped = await WriteManyAsync(path);

            // The reader still has the file it opened: the whole old document, never a mix.
            Assert.Equal(-1, SequenceOf(await ReadWholeAsync(held)));
        }

        _output.WriteLine($"Dropped writes with a reader holding the file (FileShare.Delete): {dropped} of {Writes}");
        Assert.Equal(0, dropped);
        Assert.Equal(Writes - 1, SequenceOf(await File.ReadAllTextAsync(path)));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public async Task AShareDeletePollerDropsNoWritesAndReadsOnlyCompleteDocuments()
    {
        var (dropped, reads, final) = await WriteWhilePollingAsync(FileShare.ReadWrite | FileShare.Delete, holdMs: 10, gapMs: 0);

        _output.WriteLine($"Dropped writes under a FileShare.Delete poller: {dropped} of {Writes} ({reads} complete reads)");
        Assert.Equal(0, dropped);
        Assert.Equal(Writes - 1, final);
        Assert.True(reads > 0);
    }

    [Fact]
    public async Task APollerWithoutShareDeleteDropsNoWritesAndReadsOnlyCompleteDocuments()
    {
        // FileShare.ReadWrite alone blocks any replace while it is open; the writer waits for the gaps between reads.
        var (dropped, reads, final) = await WriteWhilePollingAsync(FileShare.ReadWrite, holdMs: 10, gapMs: 10);

        _output.WriteLine($"Dropped writes under a FileShare.ReadWrite poller: {dropped} of {Writes} ({reads} complete reads)");
        Assert.Equal(0, dropped);
        Assert.Equal(Writes - 1, final);
        Assert.True(reads > 0);
    }

    private async Task<(int Dropped, int Reads, int Final)> WriteWhilePollingAsync(FileShare share, int holdMs, int gapMs)
    {
        var path = _directory.File("project.json");
        await WriteAsync(path, -1);

        using var stop = new CancellationTokenSource();
        var poller = Task.Run(async () =>
        {
            var reads = 0;
            var last = -1;
            while (!stop.IsCancellationRequested)
            {
                FileStream stream;
                try
                {
                    stream = new FileStream(path, FileMode.Open, FileAccess.Read, share);
                }
                catch (IOException)
                {
                    // A reader that does not share delete cannot open the file during the instant of a rename, as with
                    // any other program; it tries again. Only what it reads once open is under test.
                    Thread.Sleep(1);
                    continue;
                }

                using (stream)
                {
                    // Parses or throws: a torn or empty document fails the test.
                    var sequence = SequenceOf(await ReadWholeAsync(stream));
                    Assert.True(sequence >= last, $"Read sequence {sequence} after {last}.");
                    last = sequence;
                    reads++;
                    Thread.Sleep(holdMs);
                }

                Thread.Sleep(gapMs); // 0 only yields: the file is closed for a moment between reads.
            }

            return reads;
        });

        int dropped;
        try
        {
            dropped = await WriteManyAsync(path);
        }
        finally
        {
            await stop.CancelAsync();
        }

        var completeReads = await poller;
        return (dropped, completeReads, SequenceOf(await File.ReadAllTextAsync(path)));
    }

    private static async Task<int> WriteManyAsync(string path)
    {
        var dropped = 0;
        for (var sequence = 0; sequence < Writes; sequence++)
        {
            try
            {
                await WriteAsync(path, sequence);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                dropped++;
            }
        }

        return dropped;
    }

    /// <summary>A synthetic annotations document whose first topic carries the write's sequence number.</summary>
    private static Task WriteAsync(string path, int sequence) =>
        AtomicJsonFile.WriteAsync(
            path,
            new AnnotationsDocument
            {
                Topics =
                [
                    new Topic("t1", sequence.ToString(CultureInfo.InvariantCulture), "user"),
                    new Topic("t2", new string('x', PaddingLength), "user"),
                ],
            },
            ProjectJsonContext.Default.AnnotationsDocument,
            CancellationToken.None);

    private static int SequenceOf(string json)
    {
        var document = JsonSerializer.Deserialize(json, ProjectJsonContext.Default.AnnotationsDocument)
            ?? throw new JsonException("The document was empty.");
        Assert.Equal(2, document.Topics.Count);
        Assert.Equal(PaddingLength, document.Topics[1].Label.Length);
        return int.Parse(document.Topics[0].Label, CultureInfo.InvariantCulture);
    }

    private static async Task<string> ReadWholeAsync(FileStream stream)
    {
        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);
        return await reader.ReadToEndAsync();
    }
}
